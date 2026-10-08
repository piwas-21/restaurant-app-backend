using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderAmendmentEarningRetirementRequest
{
    [JsonRequired]
    public required int ExpectedOrderVersion { get; init; }

    public long? ExpectedAccountRevision { get; init; }
}

public enum OrderAmendmentEarningRetirementState
{
    Retired = 1
}

public sealed record OrderAmendmentEarningRetirementDto(
    Guid OrderId,
    Guid AmendmentId,
    OrderAmendmentEarningRetirementState State);
