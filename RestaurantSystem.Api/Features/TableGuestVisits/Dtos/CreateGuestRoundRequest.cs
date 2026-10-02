using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed record CreateGuestRoundRequest
{
    [JsonRequired]
    public Guid OperationId { get; init; }

    [Range(1, long.MaxValue)]
    public long ExpectedAccountRevision { get; init; }

    [Required]
    public string ExpectedBasketFingerprint { get; init; } = string.Empty;
}
