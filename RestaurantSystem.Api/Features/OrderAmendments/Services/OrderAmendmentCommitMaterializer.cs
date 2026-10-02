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
    IOrderMappingService mapping, IOrderAmendmentFinancialResolution financial)
{
    internal async Task<OrderAmendmentPreparedCommit> PrepareAsync(
        Order source, OrderAmendment amendment, OrderAmendmentCommitRequest request,
        ICurrentUserService currentUser, CancellationToken cancellationToken)
    {
        var quoteRequest = OrderAmendmentJson.Deserialize<OrderAmendmentQuoteRequest>(amendment.RequestJson);
        OrderAmendmentPolicy.ValidateOrderContext(source, quoteRequest);
        OrderAmendmentPolicy.ValidateChangeAuthority(source, quoteRequest, currentUser);
        if (source.Version != request.ExpectedOrderVersion || source.Version != amendment.ExpectedOrderVersion)
            throw new ConflictException("The source order changed after the quote. Quote again.");
        var sourceDto = await mapping.MapToOrderDtoAsync(source, cancellationToken);
        var quantities = source.Items.Where(item => !item.ParentOrderItemId.HasValue)
            .ToDictionary(item => item.Id, item => item.Quantity);
        await OrderAmendmentRangeValidator.ValidateAsync(context, source.Id, quoteRequest, quantities, cancellationToken);
        var quotedChanges = DeserializeChanges(amendment.ChangesJson);
        var scopes = quotedChanges.Select(change => change.Kind switch
        {
            OrderAmendmentChangeKind.InstructionChange => new OrderAmendmentUnitScope(change.OrderItemId, 0, 0, WholeLine: true),
            _ => new OrderAmendmentUnitScope(change.OrderItemId, change.StartOrdinal, change.Quantity, WholeLine: false)
        }).ToList();
        await reservationGuard.AssertUnitsMutableAsync(source.Id, scopes, cancellationToken);
        var supplement = await supplements.BuildAsync(source, quoteRequest, cancellationToken);
        var supplementDto = ValidateSupplement(supplement, amendment.SupplementSnapshotJson);
        var changes = await changeBuilder.BuildAsync(source, sourceDto, quoteRequest, supplementDto, cancellationToken);
        if (!string.Equals(OrderAmendmentJson.ChangeFingerprint(changes),
                OrderAmendmentJson.ChangeFingerprint(quotedChanges), StringComparison.Ordinal))
            throw new ConflictException("The source-line snapshot changed after the quote. Quote again.");
        var preview = await financial.PreviewAsync(source, changes, supplement, cancellationToken);
        var quotedFinancial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson);
        if (preview != quotedFinancial)
            throw new ConflictException("The financial preview changed after the quote. Quote again.");
        return new OrderAmendmentPreparedCommit(quoteRequest, supplement, changes, preview);
    }

    private OrderDto? ValidateSupplement(Order? supplement, string? snapshotJson)
    {
        var snapshot = snapshotJson is null ? null
            : OrderAmendmentJson.Deserialize<OrderAmendmentSupplementSnapshot>(snapshotJson);
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
    OrderAmendmentQuoteRequest Request, Order? Supplement,
    List<OrderAmendmentChangeSnapshot> Changes, OrderAmendmentFinancialPreviewDto Financial);
