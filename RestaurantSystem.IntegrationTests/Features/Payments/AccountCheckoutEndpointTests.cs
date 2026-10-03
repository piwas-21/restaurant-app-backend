using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

[Collection("Database Lane 3")]
public sealed class AccountCheckoutEndpointTests(DatabaseFixture fixture)
{
    [Fact]
    public void Production_registrations_resolve_the_complete_guest_checkout_graph()
    {
        using var scope = fixture.SharedFactory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAccountCheckoutStartService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAccountCheckoutCancelService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAccountGuestCheckoutReader>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAccountPaymentReceiptReader>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAccountCheckoutReconciler>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITableGuestParticipantPaymentAuthorization>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IGuestAccountPaymentPolicy>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("checkout")]
    [InlineData("operation")]
    public async Task Anonymous_lookup_without_its_private_credential_is_unavailable_and_not_cacheable(string resource)
    {
        var route = resource == "receipt"
            ? "api/account-payment-receipts/00000000-0000-0000-0000-000000000001"
            : "api/table-guest-visits/00000000-0000-0000-0000-000000000001/account-payments/operations/00000000-0000-0000-0000-000000000002"
                + (resource == "checkout" ? "/checkout" : string.Empty);
        using var client = fixture.SharedFactory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("providerSessionId").And.NotContain("receiptCredentialHash")
            .And.NotContain("connectedAccountId");
    }
}
