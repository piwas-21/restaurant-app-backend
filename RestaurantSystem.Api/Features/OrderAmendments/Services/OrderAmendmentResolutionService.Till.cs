using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    public async Task<OrderAmendmentResolutionResultDto> ConfirmTillAsync(
        Guid operationId, ManualTillConfirmationRequest request, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        ArgumentNullException.ThrowIfNull(request);
        var paymentId = request.PaymentId;
        var reference = request.TillReference?.Trim();
        if (operationId == Guid.Empty || paymentId == Guid.Empty || reference is null
            || !OrderAmendmentTillReferencePolicy.IsValid(reference))
            throw new BadRequestException("Provide one payment and a safe till refund reference.");

        var orderId = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == operationId)
            .Select(value => (Guid?)value.SourceOrderId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();

        await using (var scope = await OrderAccountMutationScope.BeginAsync(
                         context, orderId, cancellationToken))
        {
            var operation = await context.OrderAmendmentResolutionOperations
                .Include(value => value.Legs).ThenInclude(value => value.Attempts)
                .SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken)
                ?? throw Unavailable();
            if (operation.ActorUserId != actorId || operation.SourceOrderId != orderId
                || operation.ActorRole != UserRole.Admin.ToString())
                throw Unavailable();

            var leg = operation.Legs.SingleOrDefault(value => value.SourcePaymentId == paymentId)
                ?? throw Unavailable();
            if (leg.Custody != OrderAmendmentRefundCustody.ManualTill)
                throw new ConflictException("This payment does not have a local till refund step.");
            if (!AccountAmendmentRefundIntegrity.HasNoProviderContext(leg))
                throw new ConflictException("The till refund step contains conflicting provider context.");

            var evidence = await context.OrderAmendmentRefundEvidence.AsNoTracking()
                .Where(value => value.RefundLegId == leg.Id)
                .ToArrayAsync(cancellationToken);
            if (evidence.Length == 1)
            {
                EnsureSameTillConfirmation(operation, leg, evidence[0], actorId, reference);
                await scope.CommitAsync(cancellationToken);
            }
            else
            {
                if (evidence.Length != 0 || operation.State == OrderAmendmentResolutionOperationState.Resolved
                    || leg.State != OrderAmendmentRefundLegState.Pending
                    || leg.ManualTillReference is not null || leg.ResolvedAt is not null || leg.Attempts.Count != 0)
                    throw new ConflictException("The till refund step already has different or incomplete evidence.");

                var now = clock.GetUtcNow().UtcDateTime;
                leg.ManualTillReference = reference;
                leg.State = OrderAmendmentRefundLegState.Succeeded;
                leg.ResolvedAt = now;
                leg.FailureCode = null;
                context.OrderAmendmentRefundEvidence.Add(NewEvidence(leg, null,
                    await NextEvidenceSequenceAsync(leg.Id, cancellationToken),
                    OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
                    OrderAmendmentRefundLegState.Succeeded, operation, actorId, now,
                    reference, null, currentUser.GetAuditIdentifier()));
                await RefreshOperationStateAsync(operation, cancellationToken);
                operation.UpdatedAt = now;
                operation.UpdatedBy = currentUser.GetAuditIdentifier();
                await context.SaveChangesAsync(cancellationToken);
                await scope.CommitAsync(cancellationToken);
            }
        }

        await finalizer.TryFinalizeAsync(operationId, actorId, cancellationToken);
        return await ReadResultAsync(operationId, cancellationToken);
    }

    private static void EnsureSameTillConfirmation(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundEvidence evidence, Guid actorId, string reference)
    {
        if (evidence.Kind != OrderAmendmentRefundEvidenceKind.ManualTillConfirmation
            || evidence.State != OrderAmendmentRefundLegState.Succeeded
            || evidence.AmountMinor != leg.AmountMinor || evidence.Currency != leg.Currency
            || evidence.TillReference != reference || leg.ManualTillReference != reference
            || leg.State != OrderAmendmentRefundLegState.Succeeded || leg.ResolvedAt != evidence.ObservedAt
            || evidence.ActorUserId != actorId || evidence.ActorUserId != operation.ActorUserId
            || evidence.ActorRole != operation.ActorRole || leg.Attempts.Count != 0)
            throw new ConflictException("The till refund step is already bound to different evidence.");
    }
}
