using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Integrity of a PII-free prepared attempt; original session/principal is still required.</summary>
public sealed class CustomerReservationProofService(IDataProtectionProvider protection) : ICustomerReservationProofService
{
    public const int MaximumProofLength = 4096;
    private IDataProtector Protector => protection.CreateProtector("RestaurantSystem.CustomerReservationPreparation.v1");
    public string Create(Guid tenantId, Guid operationId, CustomerReservationOperationOwner owner) =>
        Protector.Protect(JsonSerializer.Serialize(new Binding(1, tenantId, operationId, owner.OwnerHash, owner.PayloadHash)));

    public void Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner)
    {
        if (string.IsNullOrWhiteSpace(proof.OwnerProof) || proof.OwnerProof.Length > MaximumProofLength) throw Invalid();
        Binding? binding;
        try
        {
            binding = JsonSerializer.Deserialize<Binding>(Protector.Unprotect(proof.OwnerProof));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            throw Invalid();
        }
        if (binding != new Binding(1, proof.TenantId, proof.OperationId, owner.OwnerHash, proof.PayloadHash)) throw Invalid();
    }

    private static NotFoundException Invalid() => new("Customer reservation operation unavailable.");
    // No time limit: durable recovery depends on the existing persisted DataProtection key ring.
    private sealed record Binding(int SchemaVersion, Guid TenantId, Guid OperationId, string OwnerHash, string PayloadHash);
}
