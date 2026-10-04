using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionQuoteFactory
{
    internal static OrderAmendmentResolutionQuoteDto Create(Guid orderId, Guid amendmentId,
        Guid actorId, OrderAmendmentResolutionQuoteRequest request,
        OrderAmendmentResolutionPlan plan, DateTime expiresAt)
    {
        var hash = OrderAmendmentResolutionFingerprint.QuoteHash(
            actorId, orderId, amendmentId, request, expiresAt, plan);
        var legs = plan.Legs.OrderBy(value => value.Payment.Id).Select(value =>
            new OrderAmendmentRefundLegQuoteDto(value.Payment.Id,
                value.Payment.PaymentMethod.ToString(), value.Custody.ToString(), value.AmountMinor,
                value.Custody == OrderAmendmentRefundCustody.ManualTill,
                value.Scopes.OrderBy(scope => scope.AllocationId).ThenBy(scope => scope.StartOrdinal)
                    .Select(scope => new RefundScopeQuoteDto(scope.AllocationId, scope.OrderItemId,
                        scope.StartOrdinal, scope.UnitCount, scope.MinorPerUnit, scope.AmountMinor)).ToArray()))
            .ToArray();
        return new OrderAmendmentResolutionQuoteDto(orderId, amendmentId,
            request.ClientOperationId, hash, expiresAt, plan.Currency, plan.CreditMinor,
            plan.RefundMinor, plan.UnpaidWaivedMinor, legs);
    }
}
