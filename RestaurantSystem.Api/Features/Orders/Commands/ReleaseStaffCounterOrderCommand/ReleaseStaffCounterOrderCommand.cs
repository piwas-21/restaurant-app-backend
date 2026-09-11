using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;

/// <summary>Releases a held order to the kitchen using an expected order revision.</summary>
public record ReleaseStaffCounterOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    [JsonIgnore]
    public Guid OrderId { get; set; }

    [JsonRequired]
    public Guid ClientOperationId { get; set; }

    [JsonRequired]
    public int ExpectedVersion { get; set; }
}
