using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    public async Task<OrderAmendmentResolutionRecoveryDto> RecoverForAmendmentAsync(
        Guid orderId, Guid amendmentId, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        if (orderId == Guid.Empty || amendmentId == Guid.Empty)
            throw Unavailable();
        var operations = await ReadRecoveryOperationsAsync(value => value.SourceOrderId == orderId
                && value.AmendmentId == amendmentId && value.ActorUserId == actorId,
            cancellationToken);
        if (operations.Length == 0)
            throw Unavailable();
        if (operations.Length != 1)
            throw new ConflictException("The amendment has ambiguous financial recovery records.");
        return await MapRecoveryAsync(operations[0], actorId, orderId, amendmentId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderAmendmentResolutionRecoveryDto>> ListRecoverableForOrderAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        if (orderId == Guid.Empty)
            throw Unavailable();
        var operations = await ReadRecoveryOperationsAsync(value => value.SourceOrderId == orderId
                && value.ActorUserId == actorId
                && value.State != OrderAmendmentResolutionOperationState.Resolved,
            cancellationToken);
        if (operations.Length > 1)
            throw new ConflictException("The order has ambiguous unresolved financial recovery records.");
        if (operations.Length == 0)
            return [];
        var operation = operations[0];
        return [await MapRecoveryAsync(operation, actorId, orderId,
            operation.AmendmentId, cancellationToken)];
    }

    private Task<OrderAmendmentResolutionOperation[]> ReadRecoveryOperationsAsync(
        System.Linq.Expressions.Expression<Func<OrderAmendmentResolutionOperation, bool>> predicate,
        CancellationToken cancellationToken) => context.OrderAmendmentResolutionOperations.AsNoTracking()
        .Where(predicate).OrderBy(value => value.StartedAt).ThenBy(value => value.Id).Take(2)
        .Include(value => value.Legs).ThenInclude(value => value.Attempts)
        .Include(value => value.Legs).ThenInclude(value => value.CashRefundIntent)
        .ToArrayAsync(cancellationToken);

    private async Task<OrderAmendmentResolutionRecoveryDto> MapRecoveryAsync(
        OrderAmendmentResolutionOperation operation, Guid actorId, Guid orderId, Guid amendmentId,
        CancellationToken cancellationToken)
    {
        if (operation.ActorUserId != actorId || operation.SourceOrderId != orderId
            || operation.AmendmentId != amendmentId || operation.ActorRole != UserRole.Admin.ToString())
            throw Unavailable();
        var snapshot = ReadRecoverySnapshot(operation.SnapshotJson);
        var request = snapshot.OriginalRequest;
        var quote = snapshot.ReviewedQuote;
        if (request?.Quote is null || request.Quote.ManualRefunds is null || quote is null)
            throw new ConflictException("The original reviewed refund evidence is incomplete; keep this operation held.");
        try
        {
            ValidateRecoverySnapshot(operation, snapshot, request, quote, actorId, orderId, amendmentId);
            var planFingerprint = OrderAmendmentResolutionPlanFingerprint.Create(operation, operation.Legs.ToArray());
            if (!string.Equals(snapshot.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
                throw new ConflictException("The frozen refund legs differ from the reviewed recovery quote.");
            ValidateQuoteLegs(operation, request, quote);
        }
        catch (JsonException exception)
        {
            throw IncompleteSnapshot(exception);
        }
        catch (OverflowException exception)
        {
            throw IncompleteSnapshot(exception);
        }
        return new OrderAmendmentResolutionRecoveryDto(request, quote,
            await ReadResultAsync(operation.Id, cancellationToken));
    }

    private static OrderAmendmentResolutionSnapshot ReadRecoverySnapshot(string json)
    {
        try
        {
            return OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(json);
        }
        catch (JsonException exception)
        {
            throw IncompleteSnapshot(exception);
        }
    }

    private static ConflictException IncompleteSnapshot(Exception exception) =>
        new("The persisted refund recovery evidence is incomplete or invalid; keep this operation held.",
            exception);

    private static void ValidateRecoverySnapshot(
        OrderAmendmentResolutionOperation operation, OrderAmendmentResolutionSnapshot snapshot,
        OrderAmendmentResolutionStartRequest request, OrderAmendmentResolutionQuoteDto quote,
        Guid actorId, Guid orderId, Guid amendmentId)
    {
        if (request.Quote is null || request.Quote.ManualRefunds is null
            || request.Quote.ManualRefunds.Any(value => value is null || value.PaymentId == Guid.Empty
                || value.AmountMinor <= 0)
            || string.IsNullOrWhiteSpace(request.Quote.Currency)
            || request.QuoteHash is null || request.QuoteHash.Length != 64
            || request.ExpiresAt.Kind != DateTimeKind.Utc
            || quote.RefundLegs is null || quote.RefundLegs.Any(value => value is null || value.Scopes is null))
            throw new ConflictException("The persisted reviewed refund request has an invalid shape.");
        var requestHash = OrderAmendmentResolutionFingerprint.RequestHash(
            actorId, orderId, amendmentId, request);
        if (snapshot.RequestHash != operation.RequestHash || requestHash != operation.RequestHash
            || request.Quote.ClientOperationId != operation.ClientOperationId
            || request.Quote.ExpectedOrderVersion != operation.ExpectedOrderVersion
            || request.Quote.ExpectedAccountRevision != operation.ExpectedAccountRevision
            || request.Quote.Currency != operation.Currency
            || request.QuoteHash != snapshot.QuoteHash || request.ExpiresAt != snapshot.ExpiresAt
            || quote.OrderId != orderId || quote.AmendmentId != amendmentId
            || quote.ClientOperationId != operation.ClientOperationId
            || quote.QuoteHash != snapshot.QuoteHash || quote.ExpiresAt != snapshot.ExpiresAt
            || quote.Currency != operation.Currency || quote.CreditMinor != operation.CreditMinor
            || quote.RefundMinor != operation.RefundMinor
            || quote.UnpaidWaivedMinor != operation.UnpaidWaivedMinor
            || SumRefundLegs(quote.RefundLegs) != operation.RefundMinor)
            throw new ConflictException("The persisted recovery request no longer matches its accepted operation.");
    }

    private static long SumRefundLegs(IReadOnlyList<OrderAmendmentRefundLegQuoteDto> legs)
    {
        return legs.Select(leg =>
        {
            if (leg.AmountMinor < 0)
                throw new ConflictException("A persisted refund leg has an invalid amount.");
            return leg.AmountMinor;
        }).Sum();
    }

    private static void ValidateQuoteLegs(
        OrderAmendmentResolutionOperation operation,
        OrderAmendmentResolutionStartRequest request,
        OrderAmendmentResolutionQuoteDto quote)
    {
        var legs = operation.Legs.OrderBy(value => value.SourcePaymentId).ToArray();
        var quoteLegs = quote.RefundLegs.OrderBy(value => value.PaymentId).ToArray();
        if (legs.Length != quoteLegs.Length
            || legs.Where((leg, index) => !QuoteLegMatches(leg, quoteLegs[index])).Any())
            throw new ConflictException("The persisted refund legs do not match the accepted review quote.");

        var manualSelections = request.Quote.ManualRefunds.OrderBy(value => value.PaymentId).ToArray();
        var manualLegs = legs.Where(value => value.Custody == OrderAmendmentRefundCustody.ManualTill
                && value.AccountPaymentAttemptId is null)
            .OrderBy(value => value.SourcePaymentId)
            .Select(value => new ManualRefundSelectionRequest
            {
                PaymentId = value.SourcePaymentId,
                AmountMinor = value.AmountMinor
            }).ToArray();
        if (!manualSelections.SequenceEqual(manualLegs))
            throw new ConflictException("The accepted manual refund selections do not match their frozen legs.");
    }

    private static bool QuoteLegMatches(
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundLegQuoteDto quote)
    {
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson)
            .OrderBy(value => value.AllocationId).ThenBy(value => value.StartOrdinal)
            .Select(value => new RefundScopeQuoteDto(value.AllocationId, value.OrderItemId,
                value.StartOrdinal, value.UnitCount, value.MinorPerUnit, value.AmountMinor)).ToArray();
        return leg.SourcePaymentId == quote.PaymentId && leg.Custody.ToString() == quote.Custody
            && leg.AmountMinor == quote.AmountMinor
            && (leg.Custody == OrderAmendmentRefundCustody.ManualTill) == quote.RequiresTillConfirmation
            && scopes.SequenceEqual(quote.Scopes)
            && OrderAmendmentCashRefundMapper.Map(leg.CashRefundIntent) == quote.CashRefund;
    }
}
