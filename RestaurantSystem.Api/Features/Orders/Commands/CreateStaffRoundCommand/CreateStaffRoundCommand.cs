using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffRoundCommand;

/// <summary>Creates one idempotent dine-in round attached to an open service session.</summary>
public record CreateStaffRoundCommand : StaffCounterOrderRequest, ICommand<ApiResponse<OrderDto>>
{
    [JsonRequired]
    public Guid ClientOperationId { get; set; }

    [JsonRequired]
    public bool ReleaseToKitchen { get; set; }
}
