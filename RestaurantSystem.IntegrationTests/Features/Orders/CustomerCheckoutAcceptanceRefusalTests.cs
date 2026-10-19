using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAcceptanceRefusalTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    [Fact]
    public async Task Prepared_payload_changes_and_foreign_recovery_do_not_disclose_or_create_an_order()
    {
        var submission = await PreparedAsync(await IntentAsync());
        var changed = submission with { Intent = submission.Intent with { Notes = "Changed after preparation" } };
        (await SubmitAsync(changed)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var first = await OrderResolutionAsync(await SubmitAsync(submission));
        var foreign = Guid.NewGuid().ToString("D");
        foreach (var response in new[] { await SubmitAsync(submission, foreign), await ResolveAsync(submission, session: foreign),
                     await ResolveAsync(submission, cancel: true, session: foreign) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().NotContain(first.OrderId!.Value.ToString());
        }
    }

    [Theory]
    [InlineData("basket")]
    [InlineData("catalogue")]
    [InlineData("channel")]
    public async Task Changed_checkout_refuses_before_acceptance_and_preserves_basket(string change)
    {
        var submission = await PreparedAsync(await IntentAsync());
        if (change == "basket")
        {
            var basket = await ReadAsync();
            await BasketAsync(await MutateAsync(basket, Update(basket)));
        }
        if (change == "catalogue")
        {
            await using var context = DatabaseFixture.CreateContext();
            (await context.Products.SingleAsync(value => value.Id == ProductId)).BasePrice = 8m;
            await context.SaveChangesAsync();
        }
        if (change == "channel") await EnableAsync(OrderType.Takeaway, false);
        (await SubmitAsync(submission)).StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
        await using var read = DatabaseFixture.CreateContext();
        (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await ReadAsync()).Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("null-contact")]
    [InlineData("null-review")]
    [InlineData("card-delivery")]
    [InlineData("missing-address")]
    public async Task Invalid_contact_and_channel_intents_are_refused_without_financial_effects(string change)
    {
        var delivery = change is "card-delivery" or "missing-address";
        var intent = await IntentAsync(delivery ? OrderType.Delivery : OrderType.Takeaway);
        intent = change switch
        {
            "name" => intent with { Contact = intent.Contact with { Name = " " } },
            "email" => intent with { Contact = intent.Contact with { Email = "invalid" } },
            "phone" => intent with { Contact = intent.Contact with { Phone = "" } },
            "null-contact" => intent with { Contact = null! },
            "null-review" => intent with { Review = null! },
            "card-delivery" => intent with { PaymentMethod = PaymentMethod.CreditCard },
            _ => intent with { Address = null }
        };
        (await SendAsync("/api/customer-mobile/checkout/prepare", intent)).StatusCode
            .Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Tenant_required_country_is_rechecked_without_seeding_form_rows_or_allocating_order_numbers()
    {
        var intent = await IntentAsync(OrderType.Delivery);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.CustomerFormFieldConfigurations.Add(new CustomerFormFieldConfiguration
            {
                FormKey = "delivery_address",
                FieldKey = "country",
                IsVisible = true,
                IsRequired = true,
                CreatedBy = nameof(CustomerCheckoutAcceptanceRefusalTests)
            });
            await context.SaveChangesAsync();
        }
        (await SendAsync("/api/customer-mobile/checkout/prepare", intent)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
        await using var read = DatabaseFixture.CreateContext();
        (await read.CustomerFormFieldConfigurations.CountAsync()).Should().Be(1);
        await PreparedAsync(intent with { Address = intent.Address! with { Country = "CH" } });
    }
}
