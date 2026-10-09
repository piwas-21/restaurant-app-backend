using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed record RecoverTableOccupancyCommand : ICommand<ApiResponse<TableOccupancyRecoveryOperationDto>>
{
    [JsonIgnore]
    public Guid TableId { get; set; }

    [JsonRequired]
    public Guid OperationId { get; init; }

    [JsonRequired]
    public Guid? ServiceSessionId { get; init; }

    [JsonRequired]
    public int ExpectedReadinessVersion { get; init; }

    public int? ExpectedSessionVersion { get; init; }
    public long? ExpectedAccountRevision { get; init; }

    [JsonRequired]
    public string PreviewFingerprint { get; init; } = string.Empty;

    [JsonRequired]
    public bool ConfirmRecovery { get; init; }

    [JsonRequired]
    public string Reason { get; init; } = string.Empty;
}
