using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionResultMapper
{
    internal static OrderAmendmentResolutionResultDto Map(
        OrderAmendmentResolutionOperation operation,
        IReadOnlyCollection<OrderAmendmentRefundLeg> legs,
        IReadOnlyCollection<OrderAmendmentRefundEvidence> evidence)
    {
        var legResults = legs.OrderBy(value => value.SourcePaymentId)
            .Select(leg => MapLeg(operation, leg,
                evidence.Where(value => value.RefundLegId == leg.Id).ToArray())).ToArray();
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved
            && operation.ResultJson is not null)
        {
            var saved = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(operation.ResultJson);
            return saved with { RefundLegs = legResults };
        }

        return new OrderAmendmentResolutionResultDto(operation.Id, operation.ClientOperationId,
            operation.AmendmentId, operation.SourceOrderId, operation.State.ToString(),
            operation.Currency, operation.CreditMinor, operation.RefundMinor,
            operation.UnpaidWaivedMinor, operation.StartedAt, operation.ResolvedAt, legResults);
    }

    private static OrderAmendmentRefundLegResultDto MapLeg(
        OrderAmendmentResolutionOperation operation,
        OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundEvidence[] evidence)
    {
        var confirmation = leg.Custody == OrderAmendmentRefundCustody.ManualTill
            ? MapTillConfirmation(operation, leg, evidence) : null;
        if (leg.Custody != OrderAmendmentRefundCustody.ManualTill
            && evidence.Any(value => value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation))
            throw new ConflictException("The refund leg contains unrelated till evidence.");
        var cashRefund = leg.CashRefundIntent;
        var cashReturn = OrderAmendmentCashRefundMapper.Map(cashRefund?.ReturnEvidence);
        if (cashRefund is not null
            && (leg.Custody != OrderAmendmentRefundCustody.ManualTill
                || cashRefund.RefundLegId != leg.Id || cashRefund.OperationId != operation.Id
                || cashRefund.ExactRefundAmountMinor != leg.AmountMinor
                || leg.State == OrderAmendmentRefundLegState.Succeeded && cashReturn is null
                || leg.State != OrderAmendmentRefundLegState.Succeeded && cashReturn is not null))
            throw new ConflictException("The cash refund attestation differs from its frozen intent.");
        return new OrderAmendmentRefundLegResultDto(leg.SourcePaymentId, leg.Custody.ToString(),
            leg.State.ToString(), leg.AmountMinor, leg.ResolvedAt, confirmation,
            OrderAmendmentCashRefundMapper.Map(cashRefund), cashReturn);
    }

    private static ManualTillConfirmationResultDto? MapTillConfirmation(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundEvidence[] evidence)
    {
        if (!AccountAmendmentRefundIntegrity.HasNoProviderContext(leg))
            throw new ConflictException("The till refund step contains conflicting provider context.");
        var manual = evidence.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation)
            .ToArray();
        if (leg.State != OrderAmendmentRefundLegState.Succeeded)
        {
            if (manual.Length != 0 || leg.ManualTillReference is not null || leg.ResolvedAt is not null)
                throw new ConflictException("A pending till refund contains unexpected confirmation evidence.");
            return null;
        }

        if (manual.Length != 1)
            throw new ConflictException("A successful till refund lacks one canonical confirmation.");
        var saved = manual[0];
        if (!OrderAmendmentTillReferencePolicy.IsValid(saved.TillReference)
            || saved.TillReference != leg.ManualTillReference || saved.AmountMinor != leg.AmountMinor
            || saved.Currency != leg.Currency || saved.State != OrderAmendmentRefundLegState.Succeeded
            || saved.ActorUserId != operation.ActorUserId || saved.ActorRole != operation.ActorRole
            || saved.ObservedAt != leg.ResolvedAt || leg.Attempts.Count != 0)
            throw new ConflictException("The till refund confirmation differs from its frozen leg.");
        return new ManualTillConfirmationResultDto(saved.TillReference!, saved.ObservedAt);
    }
}
