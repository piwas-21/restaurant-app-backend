using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Dtos;

/// <summary>
/// What settling a checkout session tells the caller. Three fields and no more, because the
/// endpoint that will return it (S9's <c>checkout-status</c>) is ANONYMOUS — a diner coming back
/// from Stripe has no account — so anything here is readable by anyone holding a session id.
/// </summary>
/// <remarks>
/// Notably absent: the amount. The diner just saw it on Stripe's own page, and repeating it here
/// would put an order's total behind nothing but a guessable identifier.
/// </remarks>
public record CheckoutSettlementDto
{
    public required string OrderNumber { get; init; }

    /// <summary>The ORDER's aggregate payment state, not the tender's — <c>Pending</c> until settled.</summary>
    public required string PaymentStatus { get; init; }

    public required string OrderStatus { get; init; }

    /// <summary>
    /// Reads the three fields off the order itself, so the answer can never describe a state the
    /// order is not in.
    /// </summary>
    public static CheckoutSettlementDto From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new CheckoutSettlementDto
        {
            OrderNumber = order.OrderNumber,
            // Enum NAMES, matching how every other status crosses this wire
            // (StringEnumConverterFactory) and what the frontend's unions are written against.
            PaymentStatus = order.PaymentStatus.ToString(),
            OrderStatus = order.Status.ToString(),
        };
    }
}
