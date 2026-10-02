using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ChannelOrderObserver(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options,
    IOrderResponseProjector responses, IOrderEventService events) : IChannelOrderObserver
{
    public async Task<ChannelOrderObservationDto> ObserveAsync(Guid orderId, ChannelOrderObservation observation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Same lock order as decision delivery, followed by the human queue's per-order lock.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision-claims"}, 0))", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision:" + orderId}, 0))", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM orders WHERE id = {orderId} FOR UPDATE", cancellationToken);
        var order = await context.Orders.IgnoreAutoIncludes().Include(order => order.ExternalReference)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Marketplace order not found.");
        var source = order.ExternalReference ?? throw new BadRequestException("This is not a marketplace order.");
        ChannelDecisionBinding.Require(source, options);
        if (source.Provider != observation.Provider || source.ExternalStoreId != observation.StoreId
            || source.ExternalOrderId != observation.ExternalOrderId)
            throw new ForbiddenException("The observation does not belong to this marketplace order.");
        RequireCurrent(source, observation);
        var previous = order.Status;
        var terminal = IsTerminal(observation.CanonicalState);
        // A canonical ACCEPTED read alone cannot bypass the human decision outbox or reset preparation.
        if (terminal)
        {
            order.Status = observation.CanonicalState == "FINISHED" ? OrderStatus.Completed : OrderStatus.Cancelled;
            order.IsKitchenReleased = false;
            if (order.Status == OrderStatus.Completed && !order.ActualDeliveryTime.HasValue)
                order.ActualDeliveryTime = observation.ObservedAt.UtcDateTime;
            await FinalizePendingDecision(orderId, observation, cancellationToken);
        }
        source.ExternalState = observation.CanonicalState;
        source.CanonicalHash = observation.CanonicalHash;
        source.LastEventAt = observation.ObservedAt.UtcDateTime;
        if (previous != order.Status)
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                FromStatus = previous,
                ToStatus = order.Status,
                ChangedAt = observation.ObservedAt.UtcDateTime,
                ChangedBy = "ChannelGateway",
                CreatedBy = "ChannelGateway",
                Notes = "Verified marketplace lifecycle observation.",
            });
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The marketplace order changed. Reconcile its current state."); }
        await transaction.CommitAsync(cancellationToken);
        if (previous != order.Status)
        {
            var dto = await responses.ProjectAsync(order, cancellationToken);
            await events.NotifyOrderStatusChanged(dto, previous.ToString());
            if (order.Status == OrderStatus.Completed) await events.NotifyOrderCompleted(dto);
        }
        return new(orderId, source.ExternalState, terminal);
    }

    private void RequireCurrent(ExternalOrderReference source, ChannelOrderObservation observation)
    {
        if (observation.ObservedAt.UtcDateTime < source.LastEventAt
            || observation.ObservedAt.UtcDateTime > DateTime.UtcNow.AddSeconds(options.Value.DecisionClockToleranceSeconds))
            throw new ConflictException("The canonical observation is stale or in the future.");
        if (IsTerminal(source.ExternalState) && observation.CanonicalState != source.ExternalState
            || source.ExternalState == "ACCEPTED" && observation.CanonicalState == "CREATED")
            throw new ConflictException("The observation would reverse a confirmed marketplace lifecycle.");
        if (observation.ObservedAt.UtcDateTime == source.LastEventAt && source.CanonicalHash is not null
            && (source.CanonicalHash != observation.CanonicalHash || source.ExternalState != observation.CanonicalState))
            throw new ConflictException("Conflicting canonical evidence shares the same observation timestamp.");
    }

    private async Task FinalizePendingDecision(Guid orderId, ChannelOrderObservation observation, CancellationToken cancellationToken)
    {
        var job = await context.ChannelOrderDecisions.IgnoreAutoIncludes()
            .FirstOrDefaultAsync(job => job.OrderId == orderId, cancellationToken);
        // A completed decision remains historical even when the provider later cancels the order.
        if (job is null || job.State is "Succeeded" or "Failed") return;
        job.State = job.Action == "deny" && observation.CanonicalState == "DENIED"
            || job.Action == "accept" && observation.CanonicalState == "FINISHED" ? "Succeeded" : "Failed";
        job.LastCanonicalHash = observation.CanonicalHash;
        job.LastObservedAt = observation.ObservedAt.UtcDateTime;
        job.LeaseId = null; job.LeaseUntil = null; job.LastReportHash = null;
    }

    private static bool IsTerminal(string state) => state is "CANCELED" or "DENIED" or "FINISHED";
}
