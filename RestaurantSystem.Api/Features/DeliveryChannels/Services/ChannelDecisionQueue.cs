using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ChannelDecisionQueue(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options,
    ICurrentUserService actor) : IChannelDecisionQueue
{
    public async Task<ChannelDecisionDto> QueueAsync(Guid orderId, ChannelDecisionRequest request, CancellationToken cancellationToken)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Cashier) || actor.UserId is null)
            throw new ForbiddenException("Only a signed-in cashier or admin may decide a marketplace order.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { orderId, request })));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision-operation:" + request.OperationId}, 0))", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"channel-decision:" + orderId}, 0))", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM orders WHERE id = {orderId} FOR UPDATE", cancellationToken);
        var order = await context.Orders.IgnoreAutoIncludes().Include(order => order.ExternalReference)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");
        var source = order.ExternalReference ?? throw new BadRequestException("This is not a marketplace order.");
        ChannelDecisionBinding.Require(source, options);
        var existing = await context.ChannelOrderDecisions.FirstOrDefaultAsync(job => job.OrderId == orderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OperationId != request.OperationId || existing.PayloadHash != hash)
                throw new ConflictException("A different marketplace decision already exists. Refresh its result.");
            return ChannelDecisionBinding.Map(existing);
        }
        if (order.Version != request.ExpectedVersion)
            throw new ConflictException("The order changed. Refresh before deciding it.");
        if (source.ExternalState != "CREATED" || order.Status != OrderStatus.PendingApproval || order.IsKitchenReleased)
            throw new ConflictException("Only a held, newly created marketplace order can be decided.");
        if (await context.ChannelOrderDecisions.AnyAsync(job => job.OperationId == request.OperationId, cancellationToken))
            throw new ConflictException("This operation identifier belongs to another order.");
        var now = DateTime.UtcNow;
        var job = new ChannelOrderDecision
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OperationId = request.OperationId,
            Action = request.Action,
            Reason = request.Reason,
            ExpectedVersion = request.ExpectedVersion,
            ActorRole = actor.Role.Value.ToString(),
            PayloadHash = hash,
            CreatedAt = now,
            CreatedBy = actor.GetAuditIdentifier(),
            AvailableAt = now,
        };
        context.ChannelOrderDecisions.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ChannelDecisionBinding.Map(job);
    }

    public async Task<ChannelDecisionDto?> ReadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.IgnoreAutoIncludes().Include(order => order.ExternalReference)
            .AsNoTracking().FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order not found.");
        if (order.ExternalReference is null) throw new BadRequestException("This is not a marketplace order.");
        var job = await context.ChannelOrderDecisions.AsNoTracking().FirstOrDefaultAsync(job => job.OrderId == orderId, cancellationToken);
        return job is null ? null : ChannelDecisionBinding.Map(job);
    }
}
