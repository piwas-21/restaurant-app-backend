using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationCancellationProofs(IDataProtectionProvider protection)
    : ICustomerReservationCancellationProofs
{
    private const string Purpose = "RestaurantSystem.CustomerReservationCancellation.v1";
    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public CustomerReservationProofDto Create(Guid tenantId, Guid operationId, CustomerReservationOperationOwner owner,
        CustomerReservationCancellationBinding binding)
    {
        var hash = Hash(owner.OwnerHash, binding);
        return new(1, operationId, tenantId, hash, Protector.Protect(JsonSerializer.Serialize(
            new ProtectedBinding(1, tenantId, operationId, owner.OwnerHash, hash, binding))));
    }

    public CustomerReservationCancellationBinding Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner)
    {
        if (string.IsNullOrWhiteSpace(proof.OwnerProof)
            || proof.OwnerProof.Length > CustomerReservationProofService.MaximumProofLength) throw Invalid();
        ProtectedBinding? original;
        try { original = JsonSerializer.Deserialize<ProtectedBinding>(Protector.Unprotect(proof.OwnerProof)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw Invalid(); }
        if (original?.Binding is not { } binding || binding.ReservationId == Guid.Empty || binding.Revision == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.TimeZone) || binding.TimeZone.Length > 128
            || original != new ProtectedBinding(1, proof.TenantId, proof.OperationId, owner.OwnerHash, proof.PayloadHash, binding)
            || proof.PayloadHash != Hash(owner.OwnerHash, binding)) throw Invalid();
        return binding;
    }

    private static string Hash(string ownerHash, CustomerReservationCancellationBinding binding) =>
        CustomerBasketOperationHash.Payload(new { Purpose, OwnerHash = ownerHash, Binding = binding });
    private static NotFoundException Invalid() => new("Customer reservation operation unavailable.");
    // No expiry: the original attempt remains recoverable using the persisted DataProtection key ring.
    private sealed record ProtectedBinding(int SchemaVersion, Guid TenantId, Guid OperationId,
        string OwnerHash, string PayloadHash, CustomerReservationCancellationBinding Binding);
}
