using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundProofs(IDataProtectionProvider protection) : ICustomerTableRoundProofs
{
    private const int MaximumProofLength = 4096;
    private IDataProtector Protector => protection.CreateProtector(CustomerTableRoundHashes.Purpose);
    public CustomerTableRoundProofDto Create(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner,
        CustomerTableRoundBinding binding)
    {
        if (!Valid(binding) || request.ServiceSessionId != binding.ServiceSessionId) throw Invalid();
        var hash = Hash(request, owner, binding);
        return new(1, request.OperationId, request.TenantId, hash, Protector.Protect(JsonSerializer.Serialize(
            new ProtectedBinding(1, request.TenantId, request.OperationId, owner.OwnerHash, hash, binding))));
    }
    public CustomerTableRoundBinding Require(CustomerTableRoundProofDto proof, CustomerTableRoundOwner owner)
    {
        if (proof.SchemaVersion != 1 || proof.OperationId == Guid.Empty || proof.TenantId == Guid.Empty
            || !CustomerTableRoundHashes.IsDigest(proof.PayloadHash)
            || string.IsNullOrWhiteSpace(proof.OwnerProof) || proof.OwnerProof.Length > MaximumProofLength)
            throw Invalid();
        ProtectedBinding? original;
        try { original = JsonSerializer.Deserialize<ProtectedBinding>(Protector.Unprotect(proof.OwnerProof)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw Invalid(); }
        if (original?.Binding is not { } binding || !Valid(binding)
            || original != new ProtectedBinding(1, proof.TenantId, proof.OperationId, owner.OwnerHash, proof.PayloadHash, binding))
            throw Invalid();
        return binding;
    }
    public void RequireInput(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner, CustomerTableRoundBinding binding)
    {
        if (request.ServiceSessionId != binding.ServiceSessionId || request.PayloadHash != Hash(request, owner, binding))
            throw new ConflictException("The prepared table round changed. Review the original attempt.");
    }
    private static bool Valid(CustomerTableRoundBinding binding) =>
        binding.ServiceSessionId != Guid.Empty && binding.TableId != Guid.Empty
        && binding.GuestOperationId != Guid.Empty && binding.ParticipantId != Guid.Empty
        && binding.ExpectedAccountRevision is >= 1 and <= CustomerTableRoundHashes.MaximumSafeRevision
        && CustomerTableRoundHashes.IsDigest(binding.BasketFingerprint)
        && CustomerTableRoundHashes.IsDigest(binding.ParticipantTokenHash)
        && CustomerTableRoundHashes.IsDigest(binding.QuoteFingerprint)
        && binding.Currency is { Length: 3 } && CurrencyCode.Normalize(binding.Currency) == binding.Currency;
    private static string Hash(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner, CustomerTableRoundBinding binding) =>
        CustomerBasketOperationHash.Payload(new
        {
            CustomerTableRoundHashes.Purpose,
            owner.OwnerHash,
            Binding = binding,
            Request = request with { PayloadHash = null, OwnerProof = null }
        });
    private static NotFoundException Invalid() => new("Customer table round operation unavailable.");
    // Non-expiring, contact-free proof. Recovery requires the original DataProtection key ring.
    private sealed record ProtectedBinding(int SchemaVersion, Guid TenantId, Guid OperationId,
        string OwnerHash, string PayloadHash, CustomerTableRoundBinding Binding);
}
