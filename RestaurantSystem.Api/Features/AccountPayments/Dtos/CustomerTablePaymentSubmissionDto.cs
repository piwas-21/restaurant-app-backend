using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePaymentSubmissionDto
{
    [JsonRequired] public required CustomerTablePaymentIntentDto Intent { get; init; }
    [JsonRequired] public required CustomerTablePaymentProofDto Proof { get; init; }
}
