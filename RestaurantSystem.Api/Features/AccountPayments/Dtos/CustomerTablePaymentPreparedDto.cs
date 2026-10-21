using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record CustomerTablePaymentPreparedDto(int SchemaVersion, CustomerTablePaymentProofDto Proof,
    Guid ServiceSessionId, long AccountRevision, AccountPaymentMode Mode, long AmountMinor, string Currency,
    IReadOnlyList<AccountPaymentAllocationDto> Allocations);

public sealed record CustomerTablePaymentProofDto(int SchemaVersion, Guid TenantId, Guid OperationId,
    string PayloadHash, string OwnerProof);

public sealed record CustomerTablePaymentBinding(Guid ServiceSessionId, Guid TableId, Guid ParticipantId,
    string ParticipantTokenHash, Guid GuestOperationId, long AccountRevision, long AmountMinor,
    string Currency, string ScopeHash);
