using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;

/// <summary>Builds the same server-priced order shape as create without persisting it.</summary>
public record QuoteStaffCounterOrderCommand : StaffCounterOrderRequest, ICommand<ApiResponse<OrderDto>>;
