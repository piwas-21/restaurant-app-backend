using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class CustomerHostedCheckoutSettingsTests : NativeCheckoutProviderFixture
{
    [Fact]
    public void Missing_configuration_is_inert_and_canonical_config_builds_the_expected_return()
    {
        new CustomerHostedCheckoutSettings().IsValid().Should().BeTrue();
        new CustomerHostedCheckoutSettings().Enabled.Should().BeFalse();
        var settings = new CustomerHostedCheckoutSettings
        {
            Enabled = true,
            LinkOrigin = "https://links.example.test",
            TenantSlug = "fixture",
            HostedPageOrigins = ["https://checkout.stripe.com"]
        };
        settings.IsValid().Should().BeTrue();
        NativeCheckoutRouting.Create(settings.LinkOrigin, settings.TenantSlug, OrderId, OperationId, new(Account, false))
            .ReturnUrl.Should().Be(Return);
    }

    [Theory]
    [InlineData("http://links.example.test", "fixture")]
    [InlineData("https://links.example.test/", "fixture")]
    [InlineData("https://links.example.test/return", "fixture")]
    [InlineData("https://links.example.test?token=private", "fixture")]
    [InlineData("https://links.example.test#private", "fixture")]
    [InlineData("https://links.example.test:8443", "fixture")]
    [InlineData("https://user:secret@links.example.test", "fixture")] // pragma: allowlist secret -- synthetic rejection fixture
    [InlineData("https://links.example.test", "../other")]
    [InlineData("https://links.example.test", "Fixture")]
    [InlineData("https://links.example.test", "fixture--other")]
    [InlineData("https://links.example.test", "")]
    public void Enabled_malformed_routing_refuses_startup_and_request_preparation(string origin, string slug)
    {
        new CustomerHostedCheckoutSettings { Enabled = true, LinkOrigin = origin, TenantSlug = slug }
            .IsValid().Should().BeFalse();
        FluentActions.Invoking(() => NativeCheckoutRouting.Create(origin, slug, OrderId, OperationId, new(Account, false)))
            .Should().Throw<BadRequestException>();
    }
}
