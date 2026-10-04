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

internal sealed class OrderAmendmentCommitMaterializer(
    ApplicationDbContext context, IOrderAmendmentReservationGuard reservationGuard,
    OrderAmendmentSupplementBuilder supplements, OrderAmendmentChangeBuilder changeBuilder,
    IOrderMappingService mapping, IOrderDisplayCurrencyResolver currencyResolver,
    IOrderAmendmentFinancialResolution financial)
{
    internal async Task<OrderAmendmentPreparedCommit> PrepareAsync(
        Order source, OrderAmendment amendment, OrderAmendmentCommitRequest request,
        ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        var quoteRequest = OrderAmendmentJson.Deserialize<OrderAmendmentQuoteRequest>(amendment.RequestJson);
        var refundAuthority = await OrderAmendmentRefundAuthorityReader.ReadAsync(
            context, source, currencyResolver, cancellationToken);
        OrderAmendmentPolicy.ValidateOrderContext(source, quoteRequest, refundAuthority);
        OrderAmendmentPolicy.ValidateChangeAuthority(source, quoteRequest, currentUser);
        if (source.Version != request.ExpectedOrderVersion || source.Version != amendment.ExpectedOrderVersion)
            throw new ConflictException("The source order changed after the quote. Quote again.");
        var sourceDto = await mapping.MapToOrderDtoAsync(source, cancellationToken);
        var quantities = source.Items.Where(item => !item.ParentOrderItemId.HasValue)
            .ToDictionary(item => item.Id, item => item.Quantity);
        await OrderAmendmentRangeValidator.ValidateAsync(context, source.Id, quoteRequest, quantities, cancellationToken);
        var quotedChanges = DeserializeChanges(amendment.ChangesJson);
        var quotedSupplement = amendment.SupplementSnapshotJson is null
            ? null
            : OrderAmendmentJson.Deserialize<OrderAmendmentSupplementSnapshot>(
                amendment.SupplementSnapshotJson);
        var supplement = await supplements.BuildForAcceptanceAsync(
            source, quoteRequest, cancellationToken, quotedSupplement?.Currency);
        var supplementDto = ValidateSupplement(supplement?.Order, quotedSupplement);
        var changes = await changeBuilder.BuildAsync(source, sourceDto, quoteRequest, supplementDto, cancellationToken);
        if (!string.Equals(OrderAmendmentJson.ChangeFingerprint(changes),
                OrderAmendmentJson.ChangeFingerprint(quotedChanges), StringComparison.Ordinal))
            throw new ConflictException("The source-line snapshot changed after the quote. Quote again.");
        var preview = await financial.PreviewAsync(source, changes, supplement?.Order, cancellationToken);
        var quotedFinancial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson);
        if (preview != quotedFinancial)
            throw new ConflictException("The financial preview changed after the quote. Quote again.");
        var mayResolveCapturedCredit = preview.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending
            && preview.CreditState == OrderAmendmentCreditState.PendingAllocationReview
            && preview.PotentialCreditMinor > 0;
        var scopes = quotedChanges.Select(change => new OrderAmendmentUnitScope(
            change.OrderItemId,
            change.Kind == OrderAmendmentChangeKind.InstructionChange ? 0 : change.StartOrdinal,
            change.Kind == OrderAmendmentChangeKind.InstructionChange ? 0 : change.Quantity,
            WholeLine: change.Kind == OrderAmendmentChangeKind.InstructionChange,
            AllowCapturedReversal: mayResolveCapturedCredit
                && change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace)).ToList();
        await reservationGuard.AssertUnitsMutableAsync(source.Id, scopes, cancellationToken);
        return new OrderAmendmentPreparedCommit(quoteRequest, supplement, changes, preview);
    }

    private OrderDto? ValidateSupplement(
        Order? supplement, OrderAmendmentSupplementSnapshot? snapshot)
    {
        if (supplement is null)
        {
            if (snapshot is not null) throw PriceChanged();
            return null;
        }
        if (snapshot is null) throw PriceChanged();
        OrderAmendmentSnapshots.RestoreSupplementIdentity(supplement, snapshot);
        var dto = OrderAmendmentSnapshots.MapBuiltOrder(mapping, supplement);
        if (!string.Equals(snapshot.PricingFingerprint,
                OrderAmendmentJson.PricingFingerprint(dto), StringComparison.Ordinal))
            throw PriceChanged();
        return dto;
    }

    private static ConflictException PriceChanged() => new(
        "The supplement price changed after the quote. Quote the order again.");

    private static List<OrderAmendmentChangeSnapshot> DeserializeChanges(string json) =>
        OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json);
}

internal sealed record OrderAmendmentPreparedCommit(
    OrderAmendmentQuoteRequest Request, OrderAmendmentSupplementBuild? Supplement,
    List<OrderAmendmentChangeSnapshot> Changes, OrderAmendmentFinancialPreviewDto Financial);
