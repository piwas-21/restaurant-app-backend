using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerOrderCancellationHashTests
{
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    // Independently SHA256-hashed canonical JSON: ExpectedVersion, Kind, OrderId, SchemaVersion.
    private const string Golden = "983e70adca9052e88df05af1d282b87774a37d04021056940a0a5b5f16bc4db7"; // pragma: allowlist secret
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Canonical_payload_matches_the_independent_order_version_and_action_oracle()
    {
        CustomerOrderCancellationHash.Payload(OrderId, 7).Should().Be(Golden);
        CustomerOrderCancellationHash.Payload(OrderId, 8).Should().NotBe(Golden);
        CustomerOrderCancellationHash.Payload(Guid.NewGuid(), 7).Should().NotBe(Golden);
        CustomerOrderCancellationHash.Validate(Valid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Unsupported_versions_cannot_be_prepared_or_submitted(int version)
    {
        var prepare = () => CustomerOrderCancellationHash.Payload(OrderId, version);
        prepare.Should().Throw<BadRequestException>();
        var submit = () => CustomerOrderCancellationHash.Validate(Valid() with { ExpectedVersion = version });
        submit.Should().Throw<BadRequestException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unknown_schemas_cannot_reuse_a_valid_version_one_digest(int schema)
    {
        var verify = () => CustomerOrderCancellationHash.Validate(Valid() with { SchemaVersion = schema });
        verify.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Empty_order_or_operation_ids_never_authenticate_an_attempt()
    {
        var emptyOrder = () => CustomerOrderCancellationHash.Validate(Valid() with { OrderId = Guid.Empty });
        emptyOrder.Should().Throw<BadRequestException>();
        var emptyOperation = () => CustomerOrderCancellationHash.Validate(Valid() with { OperationId = Guid.Empty });
        emptyOperation.Should().Throw<BadRequestException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("changed")]
    [InlineData("983E70ADCA9052E88DF05AF1D282B87774A37D04021056940A0A5B5F16BC4DB7")] // pragma: allowlist secret
    public void Missing_changed_or_noncanonical_digests_are_refused(string digest)
    {
        var verify = () => CustomerOrderCancellationHash.Validate(Valid() with { PayloadHash = digest });
        verify.Should().Throw<ConflictException>();
    }

    [Fact]
    public void A_customer_owner_survives_device_sessions_without_becoming_a_guest_owner()
    {
        var user = Guid.NewGuid();
        var session = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        var owner = CustomerOrderCancellationHash.Owner(user, session);
        CustomerOrderCancellationHash.Owner(user, otherSession).Should().Be(owner);
        CustomerOrderCancellationHash.Owner(Guid.NewGuid(), session).Should().NotBe(owner);
        CustomerOrderCancellationHash.Owner(null, session).Should().NotBe(owner);
        CustomerOrderCancellationHash.Owner(null, otherSession)
            .Should().NotBe(CustomerOrderCancellationHash.Owner(null, session));
    }

    [Fact]
    public void Missing_owner_evidence_is_refused_before_a_digest_is_issued()
    {
        var guest = () => CustomerOrderCancellationHash.Owner(null, Guid.Empty);
        guest.Should().Throw<BadRequestException>();
        var customer = () => CustomerOrderCancellationHash.Owner(Guid.Empty, Guid.NewGuid());
        customer.Should().Throw<BadRequestException>();
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("operationId")]
    [InlineData("orderId")]
    [InlineData("expectedVersion")]
    [InlineData("payloadHash")]
    public void Omitted_intent_members_are_not_silent_defaults(string field)
    {
        var body = JsonSerializer.SerializeToNode(Valid(), Json)!.AsObject();
        body.Remove(field).Should().BeTrue();
        var decode = () => body.Deserialize<CustomerOrderCancellationIntentDto>(Json);
        decode.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("amount")]
    [InlineData("reason")]
    [InlineData("ownerHash")]
    [InlineData("paymentMethod")]
    public void Private_and_financial_fields_cannot_be_smuggled_into_the_intent(string field)
    {
        var body = JsonSerializer.SerializeToNode(Valid(), Json)!.AsObject();
        body.Add(field, JsonValue.Create("injected"));
        var decode = () => body.Deserialize<CustomerOrderCancellationIntentDto>(Json);
        decode.Should().Throw<JsonException>();
    }

    private static CustomerOrderCancellationIntentDto Valid() => new()
    {
        SchemaVersion = 1,
        OperationId = Guid.NewGuid(),
        OrderId = OrderId,
        ExpectedVersion = 7,
        PayloadHash = Golden
    };
}
