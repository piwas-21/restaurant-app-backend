using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerTablePaymentProofTests
{
    private readonly CustomerTablePaymentProofs _proofs = new(new EphemeralDataProtectionProvider());
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static CustomerTablePaymentOwner Owner(Guid? customer = null) =>
        new(Session, customer, CustomerTablePaymentHashes.Owner(Session, customer));
    private static CustomerTablePaymentIntentDto Intent() => new()
    {
        SchemaVersion = 1,
        TenantId = Tenant,
        OperationId = Guid.NewGuid(),
        ServiceSessionId = Guid.NewGuid(),
        ExpectedAccountRevision = 7,
        Mode = AccountPaymentMode.Amount,
        AmountMinor = 350
    };
    private static CustomerTablePaymentBinding Binding(CustomerTablePaymentIntentDto intent) =>
        new(intent.ServiceSessionId, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            7, 350, "CHF", new string('b', 64));

    [Fact]
    public void Original_proof_preserves_the_exact_private_scope()
    {
        var intent = Intent(); var binding = Binding(intent); var owner = Owner();
        var proof = _proofs.Create(intent, owner, binding);
        proof.OperationId.Should().Be(intent.OperationId); proof.TenantId.Should().Be(Tenant);
        proof.PayloadHash.Should().MatchRegex("^[a-f0-9]{64}$"); proof.OwnerProof.Length.Should().BeLessThanOrEqualTo(4096);
        _proofs.Require(proof, owner).Should().Be(binding);
        _proofs.Invoking(value => value.RequireInput(intent, owner, binding, proof)).Should().NotThrow();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("ciphertext")]
    [InlineData("session")]
    [InlineData("customer")]
    [InlineData("key-ring")]
    public void Original_proof_cannot_be_reassigned(string changed)
    {
        var intent = Intent(); var owner = Owner(); var proof = _proofs.Create(intent, owner, Binding(intent));
        if (changed == "tenant") proof = proof with { TenantId = Guid.NewGuid() };
        if (changed == "operation") proof = proof with { OperationId = Guid.NewGuid() };
        if (changed == "version") proof = proof with { SchemaVersion = 2 };
        if (changed == "hash") proof = proof with { PayloadHash = new string('c', 64) };
        if (changed == "ciphertext") proof = proof with { OwnerProof = proof.OwnerProof[..^10] };
        if (changed == "session")
        {
            var other = Guid.NewGuid();
            owner = new(other, null, CustomerTablePaymentHashes.Owner(other, null));
        }
        if (changed == "customer") owner = Owner(Guid.NewGuid());
        var reader = changed == "key-ring" ? new CustomerTablePaymentProofs(new EphemeralDataProtectionProvider()) : _proofs;
        reader.Invoking(value => value.Require(proof, owner)).Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData("visit")]
    [InlineData("revision")]
    [InlineData("mode")]
    [InlineData("amount")]
    [InlineData("slot")]
    [InlineData("units")]
    public void Original_input_cannot_change_after_review(string changed)
    {
        var original = Intent(); var owner = Owner(); var binding = Binding(original);
        var proof = _proofs.Create(original, owner, binding);
        var changedIntent = changed switch
        {
            "visit" => original with { ServiceSessionId = Guid.NewGuid() },
            "revision" => original with { ExpectedAccountRevision = 8 },
            "mode" => original with { Mode = AccountPaymentMode.Full },
            "amount" => original with { AmountMinor = 351 },
            "slot" => original with { EqualSharePlanId = Guid.NewGuid(), EqualShareOrdinal = 1 },
            _ => original with { SelectedUnits = [new(Guid.NewGuid(), Guid.NewGuid(), 1)] }
        };
        _proofs.Invoking(value => value.RequireInput(changedIntent, owner, binding, proof)).Should().Throw<ConflictException>();
    }

    [Fact]
    public void Canonical_item_order_has_one_input_hash()
    {
        var first = new AccountPaymentUnitSelection(Guid.NewGuid(), Guid.NewGuid(), 1);
        var second = new AccountPaymentUnitSelection(first.OrderId, first.OrderItemId, 2);
        var intent = Intent() with { Mode = AccountPaymentMode.Items, AmountMinor = null, SelectedUnits = [second, first] };
        var binding = Binding(intent); var owner = Owner(); var proof = _proofs.Create(intent, owner, binding);
        _proofs.Invoking(value => value.RequireInput(intent with { SelectedUnits = [first, second] }, owner, binding, proof))
            .Should().NotThrow();
    }

    [Theory]
    [InlineData("table")]
    [InlineData("participant")]
    [InlineData("legacy-operation")]
    [InlineData("scope")]
    [InlineData("revision")]
    [InlineData("amount")]
    [InlineData("currency")]
    public void A_proof_cannot_authorize_a_different_bound_scope(string changed)
    {
        var intent = Intent(); var owner = Owner(); var binding = Binding(intent); var proof = _proofs.Create(intent, owner, binding);
        var different = changed switch
        {
            "table" => binding with { TableId = Guid.NewGuid() },
            "participant" => binding with { ParticipantId = Guid.NewGuid() },
            "legacy-operation" => binding with { GuestOperationId = Guid.NewGuid() },
            "scope" => binding with { ScopeHash = new string('d', 64) },
            "revision" => binding with { AccountRevision = 8 },
            "amount" => binding with { AmountMinor = 351 },
            _ => binding with { Currency = "EUR" }
        };
        _proofs.Invoking(value => value.RequireInput(intent, owner, different, proof)).Should().Throw<ConflictException>();
    }
}
