using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerCheckoutIntentHashTests
{
    private static CustomerCheckoutIntentDto Intent() => new()
    {
        Review = new()
        {
            SchemaVersion = 1,
            ExpectedBasketId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ExpectedPurchaseFingerprint = new string('a', 64),
            Type = OrderType.Takeaway,
            PointsToRedeem = 0,
            Tip = 3m
        },
        ExpectedQuoteFingerprint = new string('b', 64),
        Contact = new() { Name = "Guest", Email = "native@example.test", Phone = "0100" },
        PaymentMethod = PaymentMethod.Cash
    };

    [Fact]
    public void Intent_has_an_independent_canonical_SHA256_golden_and_stable_decimal_scale()
    {
        // Python json.dumps(sort_keys=True,separators=(',',':')) + hashlib.sha256,
        // with explicit numeric enums, nullable fields and invariant tip string.
        var intent = Intent();
        CustomerCheckoutIntentHash.Compute(intent).Should().Be(
            "79e447e930f9a121d81b300f02c59638bbc67475547312f65418905ea9305f92"); // pragma: allowlist secret
        CustomerCheckoutIntentHash.Compute(intent with { Review = intent.Review with { Tip = 3.000m } })
            .Should().Be(CustomerCheckoutIntentHash.Compute(intent));
    }

    [Fact]
    public void Every_customer_choice_and_review_precondition_changes_the_operation_payload_identity()
    {
        var intent = Intent();
        var changed = new[]
        {
            intent with { Contact = intent.Contact with { Name = "Another guest" } },
            intent with { Contact = intent.Contact with { Email = "another@example.test" } },
            intent with { Contact = intent.Contact with { Phone = "0200" } },
            intent with { Notes = "No salt" }, intent with { PaymentMethod = PaymentMethod.CreditCard },
            intent with { ExpectedQuoteFingerprint = new string('c', 64) },
            intent with { Review = intent.Review with { SchemaVersion = 2 } },
            intent with { Review = intent.Review with { ExpectedBasketId = Guid.NewGuid() } },
            intent with { Review = intent.Review with { ExpectedPurchaseFingerprint = new string('d', 64) } },
            intent with { Review = intent.Review with { Type = OrderType.Delivery } },
            intent with { Review = intent.Review with { PointsToRedeem = 1 } },
            intent with { Review = intent.Review with { Tip = 4m } },
            intent with { Address = new() { Street = "Street", City = "City", PostalCode = "1000", Country = "CH", AdditionalInfo = "Ring bell" } }
        };
        var original = CustomerCheckoutIntentHash.Compute(intent);
        foreach (var choice in changed) CustomerCheckoutIntentHash.Compute(choice).Should().NotBe(original);
        var address = changed[^1];
        foreach (var updated in new[]
        {
            address.Address! with { Street = "Other street" }, address.Address! with { City = "Other city" },
            address.Address! with { PostalCode = "2000" }, address.Address! with { Country = "DE" },
            address.Address! with { AdditionalInfo = "Other instructions" }
        }) CustomerCheckoutIntentHash.Compute(address with { Address = updated }).Should().NotBe(CustomerCheckoutIntentHash.Compute(address));
    }
}
