using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ChannelDecisionDelivery(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options,
    IOrderRoutingService routing, IOrderResponseProjector responses, IOrderNotificationService notifications)
    : IChannelDecisionDelivery
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);

    public async Task<ChannelDecisionLeaseDto?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) throw new ForbiddenException("Delivery-channel decisions are disabled.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Only claims are serialized; no network call is made under this tenant-database lock.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision-claims"}, 0))", cancellationToken);
        var now = DateTime.UtcNow;
        var job = await ChannelDecisionBinding.Eligible(context.ChannelOrderDecisions.IgnoreAutoIncludes(), options)
            .Include(job => job.Order).ThenInclude(order => order.ExternalReference)
            .Where(job => job.AvailableAt <= now && (job.State == "Pending" || job.State == "Unknown"
                || job.State == "Leased" && job.LeaseUntil <= now))
            .OrderBy(job => job.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (job is null) return null;
        var source = job.Order.ExternalReference ?? throw new ConflictException("The decision lost its marketplace identity.");
        ChannelDecisionBinding.Require(source, options);
        job.LeaseId = Guid.NewGuid();
        job.LeaseUntil = now.Add(LeaseDuration);
        job.State = "Leased";
        job.Attempts++;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(job.Id, job.LeaseId.Value, job.LeaseUntil.Value, source.Provider, source.ExternalStoreId,
            source.ExternalOrderId, job.Action, job.Reason, job.Attempts);
    }

    public async Task<ChannelDecisionDto> ReportAsync(Guid decisionId, ChannelDecisionReport report, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Shares the claim lock so an expired lease cannot be replaced while its result is applied.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision-claims"}, 0))", cancellationToken);
        var job = await context.ChannelOrderDecisions.IgnoreAutoIncludes().Include(job => job.Order).ThenInclude(order => order.ExternalReference)
            .FirstOrDefaultAsync(job => job.Id == decisionId, cancellationToken)
            ?? throw new NotFoundException("Decision not found.");
        var source = job.Order.ExternalReference ?? throw new ConflictException("The decision lost its marketplace identity.");
        ChannelDecisionBinding.Require(source, options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(report)));
        if (job.LeaseId == report.LeaseId && job.LastReportHash == hash && job.State == report.State)
            return ChannelDecisionBinding.Map(job);
        var now = DateTime.UtcNow;
        if (job.State != "Leased" || job.LeaseId != report.LeaseId || job.LeaseUntil <= now)
            throw new ConflictException("This delivery lease expired or was replaced. Refresh the decision.");
        if (report.ObservedAt.UtcDateTime < source.LastEventAt || report.ObservedAt.UtcDateTime > now.Add(ClockTolerance))
            throw new BadRequestException("The canonical observation timestamp is stale or in the future.");
        var wasReleased = job.Order.IsKitchenReleased;
        ApplyCanonicalResult(job, source, report);
        var newlyReleased = !wasReleased && job.Order.IsKitchenReleased;
        if (newlyReleased)
        {
            await context.Orders.IgnoreAutoIncludes().IncludeOrderLineGraph().Include(order => order.RoutingStates)
                .Where(order => order.Id == job.OrderId).LoadAsync(cancellationToken);
            await routing.EnsureRoutesAsync(job.Order, cancellationToken);
        }
        job.LastCanonicalHash = string.IsNullOrEmpty(report.CanonicalHash) ? null : report.CanonicalHash;
        job.LastReportHash = hash;
        job.LastObservedAt = string.IsNullOrEmpty(report.CanonicalHash) ? null : report.ObservedAt.UtcDateTime;
        job.State = report.State;
        job.AvailableAt = now.Add(RetryDelay);
        // Keep the last opaque lease for exact report replay; a new claim replaces it.
        job.LeaseUntil = null;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (newlyReleased)
        {
            // Matches the established staff-release path. Held orders never emit this print-triggering event.
            var dto = await responses.ProjectAsync(job.Order, cancellationToken);
            await notifications.NotifyOrderCreatedAsync(dto);
        }
        return ChannelDecisionBinding.Map(job);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The marketplace order changed while applying its result. Refresh and reconcile it.");
        }
    }

    private void ApplyCanonicalResult(ChannelOrderDecision job, ExternalOrderReference source, ChannelDecisionReport report)
    {
        var accepted = job.Action == "accept" && report.CanonicalState is "ACCEPTED" or "FINISHED";
        var denied = job.Action == "deny" && report.CanonicalState == "DENIED";
        if (report.State == "Succeeded" && !accepted && !denied)
            throw new BadRequestException("A successful decision requires matching canonical provider evidence.");
        if (report.State != "Succeeded" && (accepted || denied))
            throw new BadRequestException("Canonical decision confirmation must be reported as succeeded.");
        if (report.CanonicalState == "CANCELED" && report.State != "Failed")
            throw new BadRequestException("A canceled provider order is a failed decision.");
        if (report.CanonicalState == "UNKNOWN") return;
        if (report.CanonicalState == "CREATED") return;
        if (!accepted && !denied && report.CanonicalState != "CANCELED")
            throw new BadRequestException("The canonical state conflicts with this decision.");
        var previous = job.Order.Status;
        job.Order.Status = report.CanonicalState switch
        {
            "ACCEPTED" => OrderStatus.Confirmed,
            "FINISHED" => OrderStatus.Completed,
            _ => OrderStatus.Cancelled,
        };
        // Terminal provider evidence never starts preparation, including a delayed finished order.
        job.Order.IsKitchenReleased = report.CanonicalState == "ACCEPTED";
        if (job.Order.IsKitchenReleased)
        {
            job.Order.KitchenReleasedAt = report.ObservedAt.UtcDateTime;
            job.Order.KitchenReleasedBy = job.CreatedBy;
        }
        source.ExternalState = report.CanonicalState;
        source.LastEventAt = report.ObservedAt.UtcDateTime;
        if (previous != job.Order.Status)
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = job.OrderId,
                FromStatus = previous,
                ToStatus = job.Order.Status,
                ChangedAt = report.ObservedAt.UtcDateTime,
                ChangedBy = "ChannelGateway",
                CreatedBy = "ChannelGateway",
                Notes = "Verified marketplace decision result.",
            });
    }
}
