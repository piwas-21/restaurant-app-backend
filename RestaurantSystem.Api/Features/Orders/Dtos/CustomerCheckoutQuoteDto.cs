using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerCheckoutQuoteDto(
    int SchemaVersion,
    Guid BasketId,
    string PurchaseFingerprint,
    string QuoteFingerprint,
    OrderType Type,
    string Currency,
    bool PriceChanged,
    CustomerCheckoutMoneyDto Money,
    IReadOnlyList<CustomerCheckoutLineDto> Items,
    IReadOnlyList<PaymentMethod> AvailablePaymentMethods);
