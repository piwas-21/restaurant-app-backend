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
        CustomerBasketPayload.Add(request with { OperationId = Guid.NewGuid() })
            .Should().Be(CustomerBasketOperationHash.Payload(request));
    }

    [Fact]
    public void Root_mutation_normalization_preserves_the_independent_accepted_digest()
    {
        var request = new CustomerBasketItemMutationDto
        {
            OperationId = Guid.Parse("22222222-2222-4222-8222-222222222222"),
            ExpectedBasketId = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            ExpectedPurchaseFingerprint = new string('a', 64),
            Action = CustomerBasketItemAction.Update,
            Quantity = 3,
            SpecialInstructions = "No salt"
        };
        // Public vector independently derived from sorted PascalCase JSON and numeric enum serialization.
        CustomerBasketPayload.Mutation(Guid.Parse("11111111-1111-4111-8111-111111111111"), request)
            .Should().Be("bab4ac8d1752c653e161580d8a2ac36fab2a4d70d44f287db799a8a76b6c2495"); // pragma: allowlist secret
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

    [Fact]
    public void Channel_request_identity_preserves_the_independent_durable_digest()
    {
        var request = new CustomerBasketChannelDto
        {
            OperationId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ExpectedBasketId = Guid.Parse("22222222-2222-4222-8222-222222222222"),
            ExpectedPurchaseFingerprint = new string('A', 64),
            OrderType = Domain.Common.Enums.OrderType.Takeaway,
            ConfirmedConflictItemIds = [Guid.Parse("33333333-3333-4333-8333-333333333333")]
        };
        // Public vector independently derived with Python sorted PascalCase JSON, numeric enum and SHA-256.
        CustomerBasketPayload.Channel(request).Should().Be("d8945bfc10e954a14f07db472994572ba117b98cecfaee13fb5ab23dcaf0bc24"); // pragma: allowlist secret
    }
}
