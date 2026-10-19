using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerCheckoutIntentDto
{
    [JsonRequired]
    public required CustomerCheckoutReviewDto Review { get; init; }
    [JsonRequired]
    public string ExpectedQuoteFingerprint { get; init; } = string.Empty;
    [JsonRequired]
    public required CustomerCheckoutContactDto Contact { get; init; }
    [JsonRequired]
    public PaymentMethod PaymentMethod { get; init; }
    public CustomerCheckoutAddressDto? Address { get; init; }
    public string? Notes { get; init; }
}
