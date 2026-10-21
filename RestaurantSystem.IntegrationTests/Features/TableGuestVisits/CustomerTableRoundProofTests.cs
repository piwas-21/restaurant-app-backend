using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class CustomerTableRoundProofTests
{
    private static CustomerTableRoundIntentDto Intent() => new()
    {
        SchemaVersion = 1,
        OperationId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ServiceSessionId = Guid.NewGuid(),
        ExpectedAccountRevision = 7,
        ExpectedBasketId = Guid.NewGuid(),
        ExpectedPurchaseFingerprint = new string('a', 64)
    };
    private static CustomerTableRoundOwner Owner(Guid? session = null, Guid? customer = null)
    {
        var original = session ?? Guid.NewGuid();
        return new(original, customer, CustomerTableRoundHashes.Owner(original, customer));
    }
    private static CustomerTableRoundBinding Binding(CustomerTableRoundIntentDto intent) => new(
        intent.ServiceSessionId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('B', 64),
        intent.ExpectedAccountRevision, intent.ExpectedPurchaseFingerprint, "CHF", new string('c', 64));

    [Fact]
    public void Original_proof_restores_the_exact_binding_and_contains_no_raw_session_or_participant_credential()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new CustomerTableRoundProofs(provider);
        var intent = Intent(); var owner = Owner(); var binding = Binding(intent);
        var proof = service.Create(intent, owner, binding);
        service.Require(proof, owner).Should().Be(binding);
        new CustomerTableRoundProofs(provider).Require(proof, owner).Should().Be(binding);
        service.RequireInput(intent with { PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof }, owner, binding);
        var content = provider.CreateProtector(CustomerTableRoundHashes.Purpose).Unprotect(proof.OwnerProof);
        content.Should().NotContain(owner.BasketSessionId.ToString()).And.NotContain("ExpiresAt");
        proof.PayloadHash.Should().MatchRegex("^[a-f0-9]{64}$");
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("operation")]
    [InlineData("tenant")]
    [InlineData("visit")]
    [InlineData("revision")]
    [InlineData("basket")]
    [InlineData("fingerprint")]
    public void Every_reviewed_input_field_is_bound_before_a_submission(string field)
    {
        var service = new CustomerTableRoundProofs(new EphemeralDataProtectionProvider());
        var intent = Intent(); var owner = Owner(); var binding = Binding(intent);
        var proof = service.Create(intent, owner, binding);
        var changed = field switch
        {
            "schema" => intent with { SchemaVersion = 2 },
            "operation" => intent with { OperationId = Guid.NewGuid() },
            "tenant" => intent with { TenantId = Guid.NewGuid() },
            "visit" => intent with { ServiceSessionId = Guid.NewGuid() },
            "revision" => intent with { ExpectedAccountRevision = 8 },
            "basket" => intent with { ExpectedBasketId = Guid.NewGuid() },
            _ => intent with { ExpectedPurchaseFingerprint = new string('d', 64) }
        };
        Action submit = () => service.RequireInput(changed with { PayloadHash = proof.PayloadHash }, owner, binding);
        submit.Should().Throw<ConflictException>();
    }
    [Theory]
    [InlineData("session")]
    [InlineData("customer")]
    [InlineData("guest-downgrade")]
    [InlineData("purpose")]
    [InlineData("schema")]
    [InlineData("operation")]
    [InlineData("tenant")]
    [InlineData("hash")]
    public void Original_recovery_cannot_be_rebound_to_another_owner_scope_or_purpose(string mode)
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new CustomerTableRoundProofs(provider);
        var owner = Owner(customer: Guid.NewGuid()); var intent = Intent(); var binding = Binding(intent);
        var proof = service.Create(intent, owner, binding);
        var changedOwner = mode switch
        {
            "session" => Owner(customer: owner.CustomerId),
            "customer" => Owner(owner.BasketSessionId, Guid.NewGuid()),
            "guest-downgrade" => Owner(owner.BasketSessionId),
            _ => owner
        };
        var changed = mode switch
        {
            "purpose" => proof with { OwnerProof = provider.CreateProtector("foreign-proof-purpose").Protect("original") },
            "schema" => proof with { SchemaVersion = 2 },
            "operation" => proof with { OperationId = Guid.NewGuid() },
            "tenant" => proof with { TenantId = Guid.NewGuid() },
            "hash" => proof with { PayloadHash = new string('f', 64) },
            _ => proof
        };
        Action recover = () => service.Require(changed, changedOwner);
        recover.Should().Throw<NotFoundException>().WithMessage("Customer table round operation unavailable.");
    }
}
