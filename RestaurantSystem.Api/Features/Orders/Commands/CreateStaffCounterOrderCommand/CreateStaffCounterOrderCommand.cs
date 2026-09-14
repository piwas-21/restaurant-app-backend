using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;

/// <summary>Authenticated, idempotent counter-order creation. The operation key is required.</summary>
public record CreateStaffCounterOrderCommand : StaffCounterOrderRequest, ICommand<ApiResponse<OrderDto>>
{
    [JsonRequired]
    public Guid ClientOperationId { get; set; }

    // Required on the wire: a missing bool must not silently choose a kitchen policy.
    [JsonRequired]
    public bool ReleaseToKitchen { get; set; }
}
