using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentPreparationBoundsTests(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    [Theory]
    [InlineData(0)]
    [InlineData(49)]
    [InlineData(1401)]
    [InlineData(9_007_199_254_740_992)]
    public async Task Refuses_nonpayable_or_inexact_amounts_before_creating_a_proof(long amount)
    {
        var intent = await PaymentIntentAsync(AccountPaymentMode.Amount);
        (await SendAsync(PreparationPath, intent with { AmountMinor = amount })).IsSuccessStatusCode.Should().BeFalse();
        PreparedScopeObserved.Should().BeFalse(); await NoPaymentEffectsAsync();
    }

    [Fact]
    public async Task Configured_unit_limit_is_checked_even_when_every_selected_unit_is_available()
    {
        var intent = await PaymentIntentAsync(AccountPaymentMode.Items);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.Items).SingleAsync(); var item = order.Items.Single();
        item.Quantity.Should().Be(2, "the independent fixture has two real payable units");
        Factory.Services.GetRequiredService<IOptions<AccountPaymentSettings>>().Value.MaximumSelectedUnits = 1;
        var response = await SendAsync(PreparationPath, intent with { SelectedUnits = [new(order.Id, item.Id, 1), new(order.Id, item.Id, 2)] });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict); PreparedScopeObserved.Should().BeFalse();
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("mode-fields")]
    [InlineData("unknown-field")]
    [InlineData("null-selection")]
    public async Task Refuses_ambiguous_wire_intent(string changed)
    {
        var intent = await PaymentIntentAsync();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(intent, JsonOptions)!.AsObject();
        if (changed == "mode-fields") body["amountMinor"] = 700;
        if (changed == "unknown-field") body["tipMinor"] = 100;
        if (changed == "null-selection") body["selectedUnits"] = null;
        (await SendAsync(PreparationPath, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        PreparedScopeObserved.Should().BeFalse(); await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("hosted")]
    [InlineData("provider")]
    [InlineData("native")]
    [InlineData("feature")]
    public async Task New_preparation_requires_current_operational_and_provider_configuration(string changed)
    {
        var intent = await PaymentIntentAsync();
        if (changed == "hosted") Factory.Services.GetRequiredService<IOptions<CustomerHostedCheckoutSettings>>().Value.Enabled = false;
        if (changed == "provider") Gateway.SetupGet(value => value.IsConfigured).Returns(false);
        if (changed == "native") WithdrawNative();
        if (changed == "feature") Features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(false);
        (await SendAsync(PreparationPath, intent)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        PreparedScopeObserved.Should().BeFalse(); await NoPaymentEffectsAsync();
    }
}
