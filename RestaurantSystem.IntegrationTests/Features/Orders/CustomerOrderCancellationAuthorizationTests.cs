using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationAuthorizationTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
    }

    [Fact]
    public async Task Staff_other_customers_and_invalid_authentication_cannot_borrow_the_original_guest_capability()
    {
        var request = await AwaitingDelayAsync();
        foreach (var role in new[] { UserRole.Admin, UserRole.Server, UserRole.Cashier, UserRole.KitchenStaff, UserRole.Customer })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, role));
            var expected = role == UserRole.Customer ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden;
            foreach (var phase in new[] { "submit", "recover" })
                (await SendAsync(CancellationPath + "/original/" + phase, request)).StatusCode.Should().Be(expected);
            (await SendAsync(CancellationPath + "/original/prepare", request.Proof)).StatusCode.Should().Be(expected);
        }
        foreach (var token in new[] { "malformed-token", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, expired: true) })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await SendAsync(CancellationPath + "/original/submit", request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await SendAsync(CancellationPath + "/original/recover", request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await SendAsync(CancellationPath + "/original/prepare", request.Proof)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        Client.DefaultRequestHeaders.Authorization = null;
        foreach (var phase in new[] { "submit", "recover" })
            (await SendAsync(CancellationPath + "/" + phase, request.Intent)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.GetAsync($"/api/customer-mobile/orders/{request.Intent.OrderId}/cancellation?schemaVersion=1"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DecisionsAsync()).Should().BeEmpty();
        (await CancelAsync(request)).Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
    }

    [Fact]
    public async Task Original_proof_session_order_and_digest_are_all_required_and_cannot_be_retargeted()
    {
        var request = await AwaitingDelayAsync();
        (await SendAsync(CancellationPath + "/original/submit", request, Guid.NewGuid().ToString("D")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(CancellationPath + "/original/submit", request with
        {
            Proof = request.Proof with { PayloadHash = new string('b', 64) }
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(CancellationPath + "/original/submit", request with
        {
            Intent = request.Intent with { OrderId = Guid.NewGuid() }
        })).StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.NotFound);
        (await SendAsync(CancellationPath + "/original/submit", request with
        {
            Intent = request.Intent with { PayloadHash = new string('b', 64) }
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DecisionsAsync()).Should().BeEmpty();
        (await CancelAsync(request)).Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
    }

    [Fact]
    public async Task Canonical_operation_cannot_be_reused_for_changed_details_or_a_removed_owner()
    {
        var request = await AwaitingDelayAsync();
        var response = await CancelAsync(request);
        var version = request.Intent.ExpectedVersion + 1;
        var changed = request with
        {
            Intent = request.Intent with
            {
                ExpectedVersion = version,
                PayloadHash = RestaurantSystem.Api.Features.Orders.Services.CustomerOrderCancellationHash.Payload(request.Intent.OrderId, version)
            }
        };
        (await SendAsync(CancellationPath + "/original/submit", changed)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ChangeAsync(request.Intent.OrderId, (_, order) => { order.IsDeleted = true; return Task.CompletedTask; });
        foreach (var phase in new[] { "submit", "recover" })
            (await SendAsync(CancellationPath + "/original/" + phase, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DecisionsAsync()).Should().ContainSingle().Which.Outcome.Should().Be(response.Outcome);
    }

    [Fact]
    public async Task Signed_history_needs_the_current_database_owner_and_original_proof_never_bypasses_it()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer));
        var request = await AwaitingDelayAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: Guid.NewGuid().ToString("D")));
        foreach (var phase in new[] { "submit", "recover" })
        {
            (await SendAsync(CancellationPath + "/" + phase, request.Intent)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await SendAsync(CancellationPath + "/original/" + phase, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await Client.GetAsync($"/api/customer-mobile/orders/{request.Intent.OrderId}/cancellation?schemaVersion=1"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DecisionsAsync()).Should().BeEmpty();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer));
        (await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/submit", request.Intent)))
            .Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
    }
}
