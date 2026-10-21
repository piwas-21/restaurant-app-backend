using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentProofs(IDataProtectionProvider protection) : ICustomerTablePaymentProofs
{
    private const int MaximumProofLength = 4096;
    private IDataProtector Protector => protection.CreateProtector(CustomerTablePaymentHashes.Purpose);

    public CustomerTablePaymentProofDto Create(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePaymentBinding binding)
    {
        if (!Valid(binding) || request.SchemaVersion != 1 || request.TenantId == Guid.Empty
            || request.OperationId == Guid.Empty || request.ServiceSessionId != binding.ServiceSessionId
            || request.ExpectedAccountRevision != binding.AccountRevision) throw Unavailable();
        var hash = Hash(request, owner, binding);
        return new(1, request.TenantId, request.OperationId, hash, Protector.Protect(JsonSerializer.Serialize(
            new ProtectedBinding(1, request.TenantId, request.OperationId, owner.OwnerHash, hash, binding))));
    }

    public CustomerTablePaymentBinding Require(CustomerTablePaymentProofDto proof, CustomerTablePaymentOwner owner)
    {
        if (proof.SchemaVersion != 1 || proof.TenantId == Guid.Empty || proof.OperationId == Guid.Empty
            || !CustomerTablePaymentHashes.IsDigest(proof.PayloadHash) || string.IsNullOrWhiteSpace(proof.OwnerProof)
            || proof.OwnerProof.Length > MaximumProofLength) throw Unavailable();
        ProtectedBinding? original;
        try { original = JsonSerializer.Deserialize<ProtectedBinding>(Protector.Unprotect(proof.OwnerProof)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw Unavailable(); }
        if (original?.Binding is not { } binding || !Valid(binding)
            || original != new ProtectedBinding(1, proof.TenantId, proof.OperationId, owner.OwnerHash, proof.PayloadHash, binding))
            throw Unavailable();
        return binding;
    }

    public void RequireInput(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePaymentBinding binding, CustomerTablePaymentProofDto proof)
    {
        if (Require(proof, owner) != binding || request.SchemaVersion != proof.SchemaVersion
            || request.TenantId != proof.TenantId || request.OperationId != proof.OperationId
            || proof.PayloadHash != Hash(request, owner, binding))
            throw new ConflictException("The prepared contribution changed. Review the original attempt.");
    }

    private static bool Valid(CustomerTablePaymentBinding binding) =>
        binding.ServiceSessionId != Guid.Empty && binding.TableId != Guid.Empty && binding.ParticipantId != Guid.Empty
        && binding.GuestOperationId != Guid.Empty
        && binding.AccountRevision is >= 1 and <= CustomerTablePaymentHashes.MaximumExactInteger
        && binding.AmountMinor is >= 1 and <= CustomerTablePaymentHashes.MaximumExactInteger
        && CustomerTablePaymentHashes.IsDigest(binding.ScopeHash)
        && CustomerTablePaymentHashes.IsDigest(binding.ParticipantTokenHash)
        && binding.Currency is { Length: 3 } && CurrencyCode.Normalize(binding.Currency) == binding.Currency;

    private static string Hash(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePaymentBinding binding) => CustomerBasketOperationHash.Payload(new
        {
            CustomerTablePaymentHashes.Purpose,
            owner.OwnerHash,
            Binding = binding,
            Request = request with
            {
                SelectedUnits = (request.SelectedUnits ?? throw new BadRequestException("Selected units are required.")).OrderBy(value => value.OrderId)
                .ThenBy(value => value.OrderItemId).ThenBy(value => value.Ordinal).ToArray()
            }
        });
    private static NotFoundException Unavailable() => new("Customer table payment operation unavailable.");
    // No contacts or raw credentials; original recovery will require this same key ring.
    private sealed record ProtectedBinding(int SchemaVersion, Guid TenantId, Guid OperationId, string OwnerHash,
        string PayloadHash, CustomerTablePaymentBinding Binding);
}
