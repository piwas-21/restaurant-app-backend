using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerOrderReceiptDto(
    int SchemaVersion, Guid OrderId, string OrderNumber, OrderType Type, DateTime OrderDate,
    OrderStatus Status, PaymentStatus PaymentStatus, string Currency, string BusinessTimeZoneId,
    DateTime? ExpectedReadyAt, DateTime? EstimatedDeliveryTime, string ConfirmationFlow,
    DateTime? ReviewDeadlineUtc, CustomerCheckoutMoneyDto Money, decimal PayableTotal,
    decimal BillingCreditAmount, decimal BillChargeAmount, decimal TotalPaid, decimal RemainingAmount,
    IReadOnlyList<CustomerCheckoutLineDto> Items);
