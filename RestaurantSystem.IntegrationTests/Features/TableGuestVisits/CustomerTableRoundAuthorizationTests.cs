using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundAuthorizationTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    private static readonly string[] Routes = ["prepare", "submit", "recover", "cancel-attempt"];
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Every_native_round_action_refuses_staff_instead_of_falling_back_to_guest(UserRole role)
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, role));
        foreach (var route in Routes)
            (await SendAsync(RoundPath + route, Body(route, intent, prepared))).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
        await AssertNoFinancialEffectsAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_or_expired_authentication_never_downgrades_to_a_guest_round(bool expired)
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var token = expired ? CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, expired: true) : "invalid-fixture-token";
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var route in Routes)
            (await SendAsync(RoundPath + route, Body(route, intent, prepared))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
        await AssertNoFinancialEffectsAsync();
    }
    [Fact]
    public async Task Customer_proof_requires_the_original_current_database_customer_and_remains_recoverable_after_accounts_withdrawal()
    {
        var owner = await SignInCustomerAsync();
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.EnabledCapabilities = ["ordering", "table-visits"];
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        (await SendAsync(RoundPath + "submit", Submission(intent, prepared))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Client.DefaultRequestHeaders.Authorization = null;
        foreach (var route in Routes.Skip(1))
            (await SendAsync(RoundPath + route, Body(route, intent, prepared))).StatusCode.Should().Be(HttpStatusCode.NotFound, route);
        await SignInCustomerAsync();
        (await ResolveRoundAsync(prepared)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        UseCustomer(owner);
        await using (var context = DatabaseFixture.CreateContext())
        {
            (await context.Users.SingleAsync(value => value.Id == owner)).Role = UserRole.Cashier;
            await context.SaveChangesAsync();
        }
        (await ResolveRoundAsync(prepared)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertNoFinancialEffectsAsync();
    }
    [Fact]
    public async Task Customer_owned_basket_still_creates_an_unpaid_shared_visit_order_without_personal_redemption_or_identity()
    {
        var owner = await SignInCustomerAsync();
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var result = await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(intent, prepared)));
        result.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync();
        order.UserId.Should().BeNull();
        order.Total.Should().Be(14m);
        order.ServiceSessionId.Should().Be(Visit.ServiceSessionId);
        (await context.Baskets.AnyAsync(value => value.UserId == owner && value.Items.Any())).Should().BeFalse();
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task A_different_canonical_installation_session_cannot_submit_recover_or_stop_the_original_proof()
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        foreach (var route in Routes.Skip(1))
            (await SendAsync(RoundPath + route, Body(route, intent, prepared), Guid.NewGuid().ToString("D")))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, route);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        await AssertNoFinancialEffectsAsync();
    }
    private static object Body(string route, CustomerTableRoundIntentDto intent, CustomerTableRoundPreparedDto prepared) => route switch
    {
        "prepare" => intent,
        "submit" => Submission(intent, prepared),
        _ => prepared.Proof
    };
}
