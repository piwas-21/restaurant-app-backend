using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services.Operations;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public sealed class CustomerBasketOperationHashTests
{
    [Fact]
    public void Existing_add_payload_digest_stays_compatible_with_accepted_receipts()
    {
        var request = new AddToBasketDto { ProductId = Guid.Parse("11111111-1111-4111-8111-111111111111"), Quantity = 2 };
        // Independently derived from sorted PascalCase JSON with explicit null fields and SHA-256.
        CustomerBasketOperationHash.Payload(request).Should().Be("d59d30e77079ee8b427257d1b61d2774b6d0c33db467924854fb27b3a21d8e23"); // pragma: allowlist secret
    }

    [Fact]
    public void Dictionary_insertion_order_does_not_change_the_request_identity()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var payload = new AddToBasketDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = 2,
            SelectedIngredients = [first, second],
            IngredientQuantities = new Dictionary<Guid, int> { [first] = 1, [second] = 2 }
        };
        var reordered = payload with { IngredientQuantities = new Dictionary<Guid, int> { [second] = 2, [first] = 1 } };
        CustomerBasketOperationHash.Payload(payload).Should().Be(CustomerBasketOperationHash.Payload(reordered));
        CustomerBasketOperationHash.Payload(payload with { Quantity = 3 }).Should().NotBe(CustomerBasketOperationHash.Payload(payload));
        CustomerBasketOperationHash.Payload(payload with { SpecialInstructions = "Without salt" }).Should().NotBe(CustomerBasketOperationHash.Payload(payload));
        CustomerBasketOperationHash.Payload(payload with { SelectedIngredients = [second, first] }).Should().NotBe(CustomerBasketOperationHash.Payload(payload));
    }

    [Fact]
    public void Legacy_and_native_writers_share_owner_locks_without_changing_legacy_session_lookup()
    {
        var session = Guid.NewGuid(); var user = Guid.NewGuid();
        CustomerBasketOperationHash.WriteOwnerLock(session.ToString("D"), null)
            .Should().Be(CustomerBasketOperationHash.OwnerLock(session, null));
        CustomerBasketOperationHash.WriteOwnerLock("legacy-session", user)
            .Should().Be(CustomerBasketOperationHash.OwnerLock(Guid.NewGuid(), user));
        CustomerBasketOperationHash.WriteOwnerLock("legacy-session", null)
            .Should().NotBe(CustomerBasketOperationHash.WriteOwnerLock("other-session", null));
    }

    [Fact]
    public void Owner_binding_uses_session_and_principal_but_customer_basket_lock_spans_devices()
    {
        var session = Guid.NewGuid(); var otherSession = Guid.NewGuid(); var user = Guid.NewGuid();
        CustomerBasketOperationHash.Owner(session, user).Should().NotBe(CustomerBasketOperationHash.Owner(session, null));
        CustomerBasketOperationHash.Owner(session, user).Should().NotBe(CustomerBasketOperationHash.Owner(otherSession, user));
        CustomerBasketOperationHash.OwnerLock(session, user).Should().Be(CustomerBasketOperationHash.OwnerLock(otherSession, user));
        CustomerBasketOperationHash.OwnerLock(session, null).Should().NotBe(CustomerBasketOperationHash.OwnerLock(otherSession, null));
        CustomerBasketOperationHash.OperationLock(session).Should().NotBe(CustomerBasketOperationHash.OwnerLock(session, null));
    }
}
