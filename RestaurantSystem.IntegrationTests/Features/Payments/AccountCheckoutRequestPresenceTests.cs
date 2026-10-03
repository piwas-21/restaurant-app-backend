using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutRequestPresenceTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Start_request_rejects_an_omitted_expected_version()
    {
        var deserialize = () => JsonSerializer.Deserialize<StartAccountCheckoutRequest>("{}", WebOptions);

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void Start_request_accepts_a_present_expected_version()
    {
        var request = JsonSerializer.Deserialize<StartAccountCheckoutRequest>("""{"expectedVersion":7}""", WebOptions);

        request!.ExpectedVersion.Should().Be(7);
    }
}
