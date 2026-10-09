using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.ClearPendingTableOrdersCommand;

public sealed record ClearPendingTableOrdersCommand : ICommand<ApiResponse<ClearedTableOrdersDto>>
{
    [JsonIgnore]
    public Guid? ServiceSessionId { get; set; }

    [JsonIgnore]
    public int? TableNumber { get; set; }

    public int? ExpectedVersion { get; init; }
}
