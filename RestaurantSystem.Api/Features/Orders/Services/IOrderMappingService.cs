using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderMappingService
{
    OrderDto MapToOrderDto(Order order);
    OrderSummaryDto MapToOrderSummaryDto(Order order);
    OrderItemDto MapToOrderItemDto(OrderItem item);
    OrderPaymentDto MapToOrderPaymentDto(OrderPayment payment);
    DeliveryAddressDto? MapToDeliveryAddressDto(OrderAddress? address);
    Task<OrderDto> MapToOrderDtoAsync(Order order, CancellationToken cancellationToken = default);
}
