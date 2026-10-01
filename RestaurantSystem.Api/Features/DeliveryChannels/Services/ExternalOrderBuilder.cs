using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

internal static class ExternalOrderBuilder
{
    internal static Order Build(ExternalOrderRequest request, bool isSandbox, string fingerprint, string audit)
    {
        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = string.Empty,
            Type = OrderType.Delivery,
            Status = OrderStatus.PendingApproval,
            IsKitchenReleased = false,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = request.MerchantTotal,
            Total = request.MerchantTotal,
            TotalPaid = request.MerchantTotal,
            RemainingAmount = 0,
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            Notes = request.Instructions,
            OrderDate = request.PlacedAt.UtcDateTime,
            CreatedAt = now,
            CreatedBy = audit,
        };
        order.ExternalReference = new ExternalOrderReference
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = request.Provider,
            ExternalStoreId = request.StoreId,
            ExternalOrderId = request.ExternalOrderId,
            ExternalDisplayId = request.DisplayId,
            ExternalState = "CREATED",
            LastEventAt = request.PlacedAt.UtcDateTime,
            Currency = request.Currency,
            MerchantTotal = request.MerchantTotal,
            ReportedTax = request.ReportedTax,
            PayloadHash = fingerprint,
            FulfillmentType = request.FulfillmentType,
            IsSandbox = isSandbox,
            CreatedAt = now,
            CreatedBy = audit,
        };
        foreach (var item in request.Items)
        {
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                ProductVariationId = item.VariationId,
                ProductName = item.Name,
                VariationName = item.VariationName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                ItemTotal = item.Total,
                SpecialInstructions = item.Instructions,
                CreatedAt = now,
                CreatedBy = audit,
            });
        }
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = request.MerchantTotal,
            Currency = request.Currency,
            PaymentMethod = PaymentMethod.OnlinePayment,
            PaymentGateway = request.Provider,
            TransactionId = request.ExternalOrderId,
            Status = PaymentStatus.Completed,
            PaymentDate = request.PlacedAt.UtcDateTime,
            CreatedAt = now,
            CreatedBy = audit,
        });
        return order;
    }
}
