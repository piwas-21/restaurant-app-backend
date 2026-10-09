using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentEligibilityService(
    ApplicationDbContext context,
    ITenantFeatures features,
    ICurrentUserService currentUser,
    IOrderDisplayCurrencyResolver currencyResolver) : IOrderAmendmentEligibilityService
{
    public async Task<OrderAmendmentEligibilityDto> GetAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        OrderAmendmentPolicy.RequireActor(currentUser);
        var source = await OrderAmendmentOrderLoader.LoadSourceAsync(context, orderId, cancellationToken)
            ?? throw new NotFoundException("The source order was not found.");
        var accountRevision = source.ServiceSession?.AccountRevision;

        if (!features.OrderAmendmentsV1)
            return NotEligible(source, accountRevision, "featureDisabled");
        if (source.Status is OrderStatus.Cancelled or OrderStatus.Refunded
            || source.PaymentStatus == PaymentStatus.Refunded)
            return NotEligible(source, accountRevision, "terminalOrder");
        if (source.ServiceSessionId.HasValue
            && source.ServiceSession?.Status != TableServiceSessionStatus.Open)
            return NotEligible(source, accountRevision, "closedAccount");
        if (await HasUnresolvedFinancialResolutionAsync(source.Id, cancellationToken))
            return NotEligible(source, accountRevision, "financialResolutionPending");

        try
        {
            await OrderBillingCreditConsistency.AssertAsync(context, [source.Id], cancellationToken);
        }
        catch (ConflictException)
        {
            return NotEligible(source, accountRevision, "financialReconciliationRequired");
        }

        OrderAmendmentRefundAuthoritySnapshot refundAuthority;
        try
        {
            refundAuthority = await OrderAmendmentRefundAuthorityReader.ReadAsync(
                context, source, currencyResolver, cancellationToken);
            OrderAmendmentPolicy.ValidateRefundActivity(source, refundAuthority);
        }
        catch (ConflictException)
        {
            return NotEligible(source, accountRevision, "refundReconciliationRequired");
        }

        if (await HasHeldPaymentScopeAsync(source.Id, source.ServiceSessionId, cancellationToken))
            return NotEligible(source, accountRevision, "paymentScopeHeld");

        var mode = source.ExternalReference is null ? "Native" : "LocalSupplementOnly";
        return new OrderAmendmentEligibilityDto(source.Id, source.Version, accountRevision, true, null, mode);
    }

    private async Task<bool> HasUnresolvedFinancialResolutionAsync(
        Guid sourceOrderId, CancellationToken cancellationToken)
    {
        var snapshots = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == sourceOrderId
                && value.State == OrderAmendmentState.Committed)
            .Select(value => value.FinancialResolutionJson)
            .ToListAsync(cancellationToken);
        return snapshots.Any(OrderAmendmentFinancialGuard.IsUnresolved);
    }

    private async Task<bool> HasHeldPaymentScopeAsync(
        Guid orderId, Guid? serviceSessionId, CancellationToken cancellationToken)
    {
        if (serviceSessionId is not Guid sessionId)
            return false;
        var states = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId
                && value.Allocations.Any(allocation => allocation.OrderId == orderId))
            .Select(value => value.State)
            .ToListAsync(cancellationToken);
        return states.Any(value => value.HoldsReservation());
    }

    private static OrderAmendmentEligibilityDto NotEligible(
        Domain.Entities.Order source, long? accountRevision, string reasonCode) =>
        new(source.Id, source.Version, accountRevision, false, reasonCode, "None");
}
