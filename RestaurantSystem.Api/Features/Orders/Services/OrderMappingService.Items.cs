using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderMappingService
{
    public OrderSummaryDto MapToOrderSummaryDto(Order order)
    {
        return new OrderSummaryDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            Type = order.Type.ToString(),
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total,
            PayableTotal = order.BillingCreditAmount > 0 ? order.PayableTotal : null,
            BillingCreditAmount = order.BillingCreditAmount,
            OrderDate = order.OrderDate,
            // Root rows only, so this stays consistent with OrderDto.Items — a 1-line combo
            // with 3 components is 1 item, not 4.
            ItemCount = order.Items?.Count(i => !i.ParentOrderItemId.HasValue) ?? 0,
            IsFocusOrder = order.Focus is not null
        };
    }

    // Single-item entry point with no surrounding order, so children can only come from the
    // ChildOrderItems navigation — and the CALLER owns whether it is populated. Mapping an
    // item off an AsNoTracking query through here yields SideItems = null, silently: issue
    // #234 verbatim. Prefer MapToOrderDto, which sources children from the order's own flat
    // Items list and is immune to that.
    public OrderItemDto MapToOrderItemDto(OrderItem item) => MapOrderItem(item, childrenByParent: null);

    // childrenByParent is the order-wide parent -> children lookup built once by
    // MapToOrderDto; null means "fall back to the navigation" (see MapToOrderItemDto).
    private OrderItemDto MapOrderItem(OrderItem item, ILookup<Guid, OrderItem>? childrenByParent)
    {
        var ingredientCustomizations = OrderIngredientCustomizations.Map(item, _logger);

        // Get KitchenType from Product or Menu's first MenuItem's Product
        string? kitchenType = item.Product?.KitchenType.ToString()
            ?? item.Menu?.MenuItems?.FirstOrDefault()?.Product?.KitchenType.ToString();

        // Map child items. A bundle component and a true add-on side share the SideItems
        // collection (#158) and are told apart by Kind — now READ FROM THE CHILD ROW rather than
        // derived from the parent's mutable product type, and used to reconcile the two different
        // things a child's stored Quantity means. See ResolveChildKind and LineQuantity (#318).
        // ChildOrderItems is initialized non-null on the entity, so no null guard here — an
        // unpopulated navigation is an EMPTY collection, which is exactly why #234 failed
        // silently rather than throwing. See MapToOrderItemDto for when this branch applies.
        var childItems = childrenByParent is not null
            ? childrenByParent[item.Id]
            : item.ChildOrderItems;

        List<OrderItemDto>? sideItems = null;
        if (childItems.Any())
        {
            sideItems = childItems
                .OrderBy(child => child.PresentationOrder.HasValue ? 0 : 1)
                .ThenBy(child => child.PresentationOrder)
                .ThenBy(child => child.CreatedAt).ThenBy(child => child.Id)
                .GroupBy(child => child.SectionId ?? child.Id).SelectMany(group => group)
                .Select(child =>
                {
                    var childDto = MapOrderItem(child, childrenByParent);
                    childDto.Kind = OrderChildRendering.DisplayKind(child, item);
                    childDto.Quantity = OrderChildRendering.LineQuantity(child, item);
                    childDto.QuantityBasis = QuantityBasis.LineTotal;
                    return childDto;
                })
                .ToList();
        }

        return new OrderItemDto
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            MenuID = item.MenuId,
            SectionId = item.SectionId,
            MenuSectionItemId = item.MenuSectionItemId,
            SuggestedSideItemId = item.SuggestedSideItemId,
            ParentComponentOrderItemId = item.ParentComponentOrderItemId,
            QuantityBasis = item.ParentOrderItemId.HasValue ? QuantityBasis.LineTotal : item.QuantityBasis ?? QuantityBasis.LineTotal,
            ConfigurationScope = item.ConfigurationScope,
            CompositionRole = item.CompositionRole,
            PresentationLabel = item.PresentationLabel,
            PresentationOrder = item.PresentationOrder,
            ProductName = item.ProductName,
            VariationName = item.VariationName,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            ItemTotal = item.ItemTotal,
            SpecialInstructions = item.SpecialInstructions,
            KitchenType = kitchenType,
            IngredientCustomizations = ingredientCustomizations,
            SideItems = sideItems
        };
    }

    public DeliveryAddressDto? MapToDeliveryAddressDto(OrderAddress? address)
    {
        if (address == null) return null;

        return new DeliveryAddressDto
        {
            Id = address.Id,
            OrderId = address.OrderId,
            UserAddressId = address.UserAddressId,
            Label = address.Label,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            City = address.City,
            State = address.State,
            PostalCode = address.PostalCode,
            Country = address.Country,
            Phone = address.Phone,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            DeliveryInstructions = address.DeliveryInstructions,
            FullAddress = address.GetFullAddress()
        };
    }

}
