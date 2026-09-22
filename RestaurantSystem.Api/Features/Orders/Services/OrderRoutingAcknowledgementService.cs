using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderRoutingAcknowledgementService : IOrderRoutingAcknowledgementService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public OrderRoutingAcknowledgementService(
        ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task ApplyAcknowledgementAsync(
        string deviceId, PrintAckDto acknowledgement, CancellationToken cancellationToken)
    {
        ValidateAcknowledgementShape(acknowledgement);
        var state = await FindAcknowledgedRouteAsync(acknowledgement, cancellationToken);
        if (state is null)
        {
            RejectUnknownOrderRoute(acknowledgement);
            return;
        }

        ValidateResolvedRoute(state, deviceId, acknowledgement);
        if (state.Status == acknowledgement.Status)
        {
            return;
        }

        if (!OrderRoutingTargetResolver.CanApply(state.Status, acknowledgement.Status))
        {
            throw new BadRequestException("The print acknowledgement is older than the route state.");
        }

        state.Status = acknowledgement.Status;
        state.FailureReason = acknowledgement.FailureReason;
        state.LastAcknowledgedAt = DateTime.UtcNow;
        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;
        state.UpdatedBy = _currentUser.GetAuditIdentifier();
    }

    private static void ValidateAcknowledgementShape(PrintAckDto acknowledgement)
    {
        if (acknowledgement.JobType == DevicePrintJobType.Order
            && acknowledgement.Status is DevicePrintStatus.Received or DevicePrintStatus.Sent)
        {
            throw new BadRequestException("Order route acknowledgements must use a final status.");
        }
    }

    private async Task<OrderRoutingState?> FindAcknowledgedRouteAsync(
        PrintAckDto acknowledgement, CancellationToken cancellationToken)
    {
        var query = _context.OrderRoutingStates
            .Where(state => state.Target == acknowledgement.Target);
        return acknowledgement.JobId.HasValue
            ? await query.SingleOrDefaultAsync(candidate =>
                candidate.JobId == acknowledgement.JobId.Value
                && candidate.Revision == acknowledgement.Revision, cancellationToken)
            : await query.SingleOrDefaultAsync(candidate =>
                candidate.OrderId == acknowledgement.OrderId, cancellationToken);
    }

    private static void RejectUnknownOrderRoute(PrintAckDto acknowledgement)
    {
        if (acknowledgement.JobType == DevicePrintJobType.Order)
        {
            throw new BadRequestException("The order print job is not known to this tenant.");
        }
    }

    private static void ValidateResolvedRoute(
        OrderRoutingState state, string deviceId, PrintAckDto acknowledgement)
    {
        if (acknowledgement.Status is DevicePrintStatus.Queued
            or DevicePrintStatus.Received
            or DevicePrintStatus.Sent)
        {
            throw new BadRequestException("Order route acknowledgements must use a final status.");
        }

        if (state.OrderId != acknowledgement.OrderId
            || state.DeviceId is null
            || state.DeviceId != deviceId)
        {
            throw new BadRequestException("The print acknowledgement does not match its route.");
        }

        if (acknowledgement.JobType is not null
            && acknowledgement.JobType != DevicePrintJobType.Order)
        {
            throw new BadRequestException("The print acknowledgement job type does not match its route.");
        }
    }
}
