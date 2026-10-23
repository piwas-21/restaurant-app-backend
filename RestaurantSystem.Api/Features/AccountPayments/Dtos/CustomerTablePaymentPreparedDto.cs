using RestaurantSystem.Domain.Common.Enums;
using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record CustomerTablePaymentPreparedDto(int SchemaVersion, CustomerTablePaymentProofDto Proof,
    Guid ServiceSessionId, long AccountRevision, AccountPaymentMode Mode, long AmountMinor, string Currency,
    IReadOnlyList<AccountPaymentAllocationDto> Allocations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePaymentProofDto([property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] Guid TenantId, [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] string PayloadHash, [property: JsonRequired] string OwnerProof);

public sealed record CustomerTablePaymentBinding(Guid ServiceSessionId, Guid TableId, Guid ParticipantId,
    string ParticipantTokenHash, Guid GuestOperationId, long AccountRevision, long AmountMinor,
    string Currency, string ScopeHash);
