using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationEditProofs(IDataProtectionProvider protection) : ICustomerReservationEditProofs
{
    private const string Purpose = "RestaurantSystem.CustomerReservationEdit.v1";
    private IDataProtector Protector => protection.CreateProtector(Purpose);
    public CustomerReservationProofDto Create(CustomerReservationEditDto request, CustomerReservationOperationOwner owner,
        CustomerReservationEditBinding binding)
    {
        var hash = Hash(request, owner, binding);
        return new(1, request.OperationId, request.TenantId, hash, Protector.Protect(JsonSerializer.Serialize(
            new ProtectedBinding(1, request.TenantId, request.OperationId, owner.OwnerHash, hash, binding))));
    }
    public CustomerReservationEditBinding Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner)
    {
        if (string.IsNullOrWhiteSpace(proof.OwnerProof) || proof.OwnerProof.Length > CustomerReservationProofService.MaximumProofLength)
            throw Invalid();
        ProtectedBinding? original;
        try { original = JsonSerializer.Deserialize<ProtectedBinding>(Protector.Unprotect(proof.OwnerProof)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw Invalid(); }
        if (original?.Binding is not { } binding || binding.ReservationId == Guid.Empty || binding.Revision == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.TimeZone) || binding.TimeZone.Length > 128
            || string.IsNullOrWhiteSpace(binding.Currency) || binding.Currency.Length > 3
            || original != new ProtectedBinding(1, proof.TenantId, proof.OperationId, owner.OwnerHash, proof.PayloadHash, binding))
            throw Invalid();
        return binding;
    }
    public void RequireInput(CustomerReservationEditDto request, CustomerReservationOperationOwner owner, CustomerReservationEditBinding binding)
    {
        if (request.ReservationId != binding.ReservationId || request.TimeZone != binding.TimeZone
            || request.PayloadHash != Hash(request, owner, binding))
            throw new ConflictException("The prepared booking change differs. Review the original attempt.");
    }
    private static string Hash(CustomerReservationEditDto request, CustomerReservationOperationOwner owner, CustomerReservationEditBinding binding) =>
        CustomerBasketOperationHash.Payload(new
        {
            Purpose,
            owner.OwnerHash,
            Binding = binding,
            Request = request with { PayloadHash = null, OwnerProof = null }
        });
    private static NotFoundException Invalid() => new("Customer reservation operation unavailable.");
    // PII-free, non-expiring authority; retain the original DataProtection key ring for recovery.
    private sealed record ProtectedBinding(int SchemaVersion, Guid TenantId, Guid OperationId,
        string OwnerHash, string PayloadHash, CustomerReservationEditBinding Binding);
}
