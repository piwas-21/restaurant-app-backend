using RestaurantSystem.Api.Features.AccountPayments.Services;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task<StartDecision> StartNewUnderLocksAsync(
        Guid orderId, Guid amendmentId, OrderAmendmentResolutionStartRequest request,
        string requestHash, Guid actorId, CancellationToken cancellationToken)
    {
        await using var scope = await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
        var now = resolutionPolicy.UtcNow;
        var identity = new OrderAmendmentResolutionRefusalIdentity(orderId, amendmentId, requestHash, actorId);
        if (request.ExpiresAt <= now)
            return await PersistRefusalAsync(identity, request,
                OrderAmendmentResolutionRefusalCodes.QuoteExpired, now, cancellationToken);
        if (request.ExpiresAt > now.Add(resolutionPolicy.QuoteLifetime))
            return await PersistRefusalAsync(identity, request,
                OrderAmendmentResolutionRefusalCodes.QuoteChanged, now, cancellationToken);

        var source = await LoadSourceAsync(orderId, cancellationToken);
        if (request.Quote.ExpectedOrderVersion != source.Version)
            return await PersistRefusalAsync(identity, request,
                OrderAmendmentResolutionRefusalCodes.SourceVersionConflict, now, cancellationToken);
        if (request.Quote.ExpectedAccountRevision != source.ServiceSession?.AccountRevision)
            return await PersistRefusalAsync(identity, request,
                OrderAmendmentResolutionRefusalCodes.AccountRevisionConflict, now, cancellationToken);

        var state = await ReadPlanningStateAsync(orderId, amendmentId, request.Quote, cancellationToken);
        var quoteHash = OrderAmendmentResolutionFingerprint.QuoteHash(actorId,
            orderId, amendmentId, request.Quote, request.ExpiresAt, state.Plan);
        if (!string.Equals(request.QuoteHash, quoteHash, StringComparison.Ordinal))
            return await PersistRefusalAsync(identity, request,
                OrderAmendmentResolutionRefusalCodes.QuoteChanged, now, cancellationToken);

        var existingForAmendment = await context.OrderAmendmentResolutionOperations
            .SingleOrDefaultAsync(value => value.AmendmentId == amendmentId, cancellationToken);
        if (existingForAmendment is not null)
        {
            RequireSameRequest(existingForAmendment, orderId, amendmentId, requestHash);
            return new(existingForAmendment.Id, null);
        }

        var operationId = PersistOperation(state, request, requestHash, actorId);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new(operationId, null);
    }

    private async Task<StartDecision> PersistRefusalAsync(
        OrderAmendmentResolutionRefusalIdentity identity, OrderAmendmentResolutionStartRequest request,
        string failureCode, DateTime now, CancellationToken cancellationToken)
    {
        var (orderId, amendmentId, requestHash, actorId) = identity;
        var refusal = new OrderAmendmentResolutionRefusal
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorId,
            SourceOrderId = orderId,
            AmendmentId = amendmentId,
            ClientOperationId = request.Quote.ClientOperationId,
            RequestHash = requestHash,
            FailureCode = failureCode,
            OriginalRequestJson = OrderAmendmentJson.Serialize(request),
            CreatedAt = PostgresTimestampPrecision.TruncateToMicrosecond(now),
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        context.OrderAmendmentResolutionRefusals.Add(refusal);
        await context.SaveChangesAsync(cancellationToken);
        return new(null, MapRefusal(refusal));
    }

    private static OrderAmendmentResolutionStartRequest NormalizeStartRequest(
        OrderAmendmentResolutionStartRequest request)
    {
        var quote = request.Quote with
        {
            ExpectedAccountRevision = request.Quote.ExpectedAccountRevision,
            ManualRefunds = request.Quote.ManualRefunds.OrderBy(value => value.PaymentId).ToArray()
        };
        return request with { Quote = quote };
    }

    private Task<OrderAmendmentResolutionRefusal?> FindRefusalByClientKeyAsync(
        Guid actorId, Guid clientOperationId, CancellationToken cancellationToken) =>
        context.OrderAmendmentResolutionRefusals.AsNoTracking().SingleOrDefaultAsync(
            value => value.ActorUserId == actorId && value.ClientOperationId == clientOperationId,
            cancellationToken);

    private static void RequireSameRefusal(OrderAmendmentResolutionRefusal refusal,
        Guid orderId, Guid amendmentId, string requestHash)
    {
        if (refusal.SourceOrderId != orderId || refusal.AmendmentId != amendmentId
            || refusal.RequestHash != requestHash)
            throw new ConflictException("This operation key is already bound to another amendment request.");
    }

    private static OrderAmendmentResolutionRefusalDto MapRefusal(
        OrderAmendmentResolutionRefusal refusal) => new(
        refusal.ActorUserId, refusal.SourceOrderId, refusal.AmendmentId,
        refusal.ClientOperationId, refusal.RequestHash, refusal.FailureCode,
        refusal.CreatedAt, OrderAmendmentJson.Deserialize<OrderAmendmentResolutionStartRequest>(
            refusal.OriginalRequestJson));

    private sealed record StartDecision(Guid? OperationId, OrderAmendmentResolutionRefusalDto? Refusal);
}
