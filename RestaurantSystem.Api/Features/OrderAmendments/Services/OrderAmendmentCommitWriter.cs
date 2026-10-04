using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentCommitWriter(
    ApplicationDbContext context, ICurrentUserService currentUser,
    IOrderAmendmentFinancialResolution financial, IOrderNativeBillingAcceptance fidelity,
    IOrderRoutingService routing, IOrderAmendmentQueryService queries,
    OrderAmendmentKitchenStager kitchenStager)
{
    internal async Task<OrderAmendmentCommitDto> StageAsync(
        Order source, OrderAmendment amendment, OrderAmendmentCommitRequest request,
        OrderAmendmentPreparedCommit prepared, string payloadHash, long? accountRevision,
        CancellationToken cancellationToken)
    {
        var supplementBuild = prepared.Supplement;
        var supplement = supplementBuild?.Order;
        if (supplement is not null)
        {
            amendment.SupplementOrderId = supplement.Id;
            context.Orders.Add(supplement);
            if (supplement.IsKitchenReleased)
                await routing.EnsureRoutesAsync(supplement, cancellationToken);
        }
        amendment.ChangesJson = OrderAmendmentJson.Serialize(prepared.Changes);
        AddSourceAuditNote(source, amendment, prepared.Request.Reason);
        amendment.ClientOperationId = request.ClientOperationId;
        amendment.CommitPayloadHash = payloadHash;
        amendment.CommittedAccountRevision = accountRevision;
        amendment.CommittedAt = DateTime.UtcNow;
        amendment.State = OrderAmendmentState.Committed;
        await financial.StageAsync(amendment, source, prepared.Financial, cancellationToken);
        await kitchenStager.StageAsync(source, amendment.Id, accountRevision, prepared.Changes, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (supplementBuild is not null)
        {
            var acceptedSupplement = supplementBuild.Order;
            var redemption = prepared.Request.PointsToRedeem is > 0
                ? await fidelity.RedeemAsync(acceptedSupplement, prepared.Request.PointsToRedeem,
                    supplementBuild.CustomerUserId,
                    cancellationToken, failOnError: true)
                : null;
            await fidelity.WriteAcceptedSnapshotAsync(
                acceptedSupplement, supplementBuild.AcceptedCurrency, supplementBuild.EarningEvaluation,
                redemption, cancellationToken);
        }
        var result = await queries.MaterializeCommittedResultAsync(amendment, cancellationToken);
        amendment.CommitResultJson = OrderAmendmentJson.Serialize(result);
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private void AddSourceAuditNote(
        Order source, OrderAmendment amendment, string? reason)
    {
        var note = string.IsNullOrWhiteSpace(reason)
            ? $"Order amendment {amendment.Id:N} committed."
            : $"Order amendment {amendment.Id:N}: {OrderAmendmentPolicy.SanitizeText(reason, 350)}";
        context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            OrderId = source.Id,
            Order = source,
            ClientOperationId = amendment.Id,
            Audience = OrderNoteAudience.Staff,
            Text = note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetAuditIdentifier()
        });
    }

}
