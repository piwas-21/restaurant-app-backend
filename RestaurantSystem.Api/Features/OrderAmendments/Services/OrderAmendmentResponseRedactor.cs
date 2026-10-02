using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResponseRedactor
{
    internal static OrderAmendmentQuoteDto RedactQuote(OrderAmendmentQuoteDto result) => result with
    {
        SourceOrder = RedactOrder(result.SourceOrder),
        SupplementOrder = result.SupplementOrder is null
            ? null
            : RedactOrder(result.SupplementOrder),
        Changes = RedactChanges(result.Changes)
    };

    internal static IReadOnlyList<OrderAmendmentChangeSnapshot> RedactChanges(
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes) =>
        changes.Select(change => change with
        {
            Previous = RedactItemInstructions(change.Previous),
            Current = change.Current is null ? null : RedactItemInstructions(change.Current)
        }).ToArray();

    internal static OrderAmendmentCommitDto RedactCommit(OrderAmendmentCommitDto result) => result with
    {
        SupplementOrder = result.SupplementOrder is null
            ? null
            : RedactOrder(result.SupplementOrder)
    };

    internal static OrderDto RedactOrder(OrderDto order)
    {
        order.GuestStatusToken = null;
        order.UserId = null;
        order.CustomerName = null;
        order.CustomerEmail = null;
        order.CustomerPhone = null;
        order.PreferredLanguage = null;
        order.Notes = null;
        order.CancellationReason = null;
        order.DeliveryAddress = null;
        order.ExternalOrder = null;
        order.PromoCode = null;
        order.FocusReason = null;
        order.FocusedBy = null;
        order.KitchenReleasedBy = null;
        order.OrderTypeOverrideBy = null;
        order.OrderTypeOverrideItems = null;
        order.Payments = [];
        order.StatusHistory = [];
        order.PermittedActions = null;
        order.RoutingStates = null;
        foreach (var item in order.Items)
        {
            RedactItemInstructions(item);
        }
        return order;
    }

    private static OrderItemDto RedactItemInstructions(OrderItemDto item)
    {
        item.SpecialInstructions = null;
        if (item.SideItems is not null)
        {
            foreach (var side in item.SideItems)
            {
                RedactItemInstructions(side);
            }
        }

        return item;
    }
}
