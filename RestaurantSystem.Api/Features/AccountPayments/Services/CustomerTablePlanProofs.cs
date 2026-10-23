using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePlanProofs(IDataProtectionProvider protection) : ICustomerTablePlanProofs
{
    internal const string Purpose = "customer-native-table-plan-v1";
    // Schema1 character ceiling matches the native decoder and preserves issued-proof recovery.
    private const int MaximumProofLength = 4096;
    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public CustomerTablePlanProofDto Create(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePlanBinding binding)
    {
        var hash = Hash(request, owner, binding);
        return new(1, request.TenantId, request.OperationId, hash, Protector.Protect(JsonSerializer.Serialize(
            new ProtectedBinding(1, request.TenantId, request.OperationId, owner.OwnerHash, hash, binding))));
    }

    public CustomerTablePlanBinding Require(CustomerTablePlanProofDto proof, CustomerTablePaymentOwner owner)
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

    public void RequireInput(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePlanBinding binding, CustomerTablePlanProofDto proof)
    {
        if (Require(proof, owner) != binding || request.SchemaVersion != proof.SchemaVersion
            || request.TenantId != proof.TenantId || request.OperationId != proof.OperationId
            || proof.PayloadHash != Hash(request, owner, binding))
            throw new ConflictException("The prepared split changed. Recover the original operation.");
    }

    private static bool Valid(CustomerTablePlanBinding value) => value.ServiceSessionId != Guid.Empty
        && value.TableId != Guid.Empty && value.ParticipantId != Guid.Empty && value.GuestOperationId != Guid.Empty
        && value.AccountRevision is >= 1 and <= CustomerTablePaymentHashes.MaximumExactInteger
        && value.TotalMinor is >= 1 and <= CustomerTablePaymentHashes.MaximumExactInteger
        && CustomerTablePaymentHashes.IsDigest(value.ParticipantTokenHash)
        && CustomerTablePaymentHashes.IsDigest(value.PlanPayloadHash)
        && CustomerTablePaymentHashes.IsDigest(value.ScopeHash) && value.RoundingIncrementMinor is 1 or 5
        && value.Currency is { Length: 3 } && CurrencyCode.Normalize(value.Currency) == value.Currency;
    private static string Hash(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePlanBinding binding) => CustomerBasketOperationHash.Payload(new { Purpose, owner.OwnerHash, Binding = binding, Request = request });
    private static NotFoundException Unavailable() => new("Customer table split operation unavailable.");
    private sealed record ProtectedBinding(int SchemaVersion, Guid TenantId, Guid OperationId,
        string OwnerHash, string PayloadHash, CustomerTablePlanBinding Binding);
}
