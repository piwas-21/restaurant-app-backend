using System.Text.Json;
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
    private const string EarningRetirementAuditIdentifier = "OrderBillingEarningRetirementService";

    public async Task<OrderAmendmentEarningRetirementDto> PrepareEarningRetirementAsync(
        Guid orderId, Guid amendmentId, OrderAmendmentEarningRetirementRequest request,
        CancellationToken cancellationToken)
    {
        RequireAdminActor();
        OrderAmendmentPolicy.RequireFeature(features);
        if (orderId == Guid.Empty || amendmentId == Guid.Empty)
            throw new BadRequestException("An order and committed amendment are required.");

        await using var scope = await OrderAccountMutationScope.BeginAsync(
            context, orderId, cancellationToken);
        var source = await LoadSourceAsync(orderId, cancellationToken);
        var amendment = await context.OrderAmendments.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == amendmentId && value.SourceOrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException("The committed amendment was not found.");
        var evidence = await OrderAmendmentLoyaltyEvidenceReader.ReadAsync(
            context, orderId, cancellationToken);
        var acceptedCurrency = await OrderNativeAcceptedCurrency.ReadOrderCurrencyEvidenceAsync(
            context, source, cancellationToken);
        var money = new AccountMoney(acceptedCurrency
            ?? source.ServiceSession?.Currency
            ?? resolutionPolicy.ResolveCurrency(source));

        if (evidence.Retirement is not null)
        {
            List<OrderAmendmentChangeSnapshot> replayChanges;
            try
            {
                replayChanges = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(
                    amendment.ChangesJson);
            }
            catch (JsonException exception)
            {
                throw new ConflictException("The committed earning retirement scope cannot be verified.", exception);
            }
            if (evidence.Retirement.AmendmentId != amendmentId
                || !OrderAmendmentEarningRetirementRules.MatchesRetirement(source, amendment,
                    replayChanges, evidence, money, out _))
                throw new ConflictException("A different or inconsistent earning retirement already exists.");
            await scope.CommitAsync(cancellationToken);
            return new(orderId, amendmentId, OrderAmendmentEarningRetirementState.Retired);
        }

        if (request.ExpectedOrderVersion != source.Version
            || request.ExpectedAccountRevision != source.ServiceSession?.AccountRevision)
            throw new ConflictException("The order account changed. Refresh the amendment context before preparing earning retirement.");

        await EnsureNoPriorUnresolvedResolutionAsync(source, amendmentId, cancellationToken);

        List<OrderAmendmentChangeSnapshot> changes;
        try
        {
            changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        }
        catch (JsonException exception)
        {
            throw new ConflictException("The committed full-source amendment scope cannot be verified.", exception);
        }
        var recalculatedFinancial = await financialResolution.PreviewAsync(
            source, changes, null, cancellationToken);
        if (!OrderAmendmentEarningRetirementRules.IsEligible(
                source, amendment, changes, evidence, money, recalculatedFinancial, out var retiredUnitCount))
            throw new ConflictException("Only an exact, unresolved full-source void can retire unknown earning evidence.");

        var snapshot = evidence.Snapshot
            ?? throw new ConflictException("The accepted earning snapshot is unavailable.");
        context.OrderBillingEarningRetirements.Add(new OrderBillingEarningRetirement
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SnapshotId = snapshot.Id,
            AmendmentId = amendmentId,
            RetiredUnitCount = retiredUnitCount,
            CreatedAt = resolutionPolicy.UtcNow,
            CreatedBy = EarningRetirementAuditIdentifier
        });
        scope.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new(orderId, amendmentId, OrderAmendmentEarningRetirementState.Retired);
    }

    private async Task<List<OrderAmendment>> EnsureNoPriorUnresolvedResolutionAsync(
        Order source, Guid amendmentId, CancellationToken cancellationToken)
    {
        var sourceAmendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id
                && value.State == OrderAmendmentState.Committed).ToListAsync(cancellationToken);
        if (sourceAmendments.Any(value => value.Id != amendmentId
                && OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve the earlier committed amendment before starting another paid correction.");
        if (source.ServiceSessionId is Guid sessionId
            && await context.OrderAmendmentResolutionOperations.AsNoTracking().AnyAsync(value =>
                value.ServiceSessionId == sessionId
                && value.State != OrderAmendmentResolutionOperationState.Resolved, cancellationToken))
            throw new ConflictException("Resolve the earlier paid correction in this table account first.");
        return sourceAmendments;
    }
}
