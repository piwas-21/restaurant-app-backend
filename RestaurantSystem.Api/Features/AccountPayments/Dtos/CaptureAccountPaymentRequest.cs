using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record CaptureAccountPaymentRequest
{
    [JsonRequired]
    public int ExpectedVersion { get; init; }

    public long? ReceivedMinor { get; init; }
}
