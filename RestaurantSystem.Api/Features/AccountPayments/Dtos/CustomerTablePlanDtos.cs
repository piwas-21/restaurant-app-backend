using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePlanIntentDto(
    [property: JsonRequired] int SchemaVersion, [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] Guid OperationId, [property: JsonRequired] Guid ServiceSessionId,
    [property: JsonRequired] long ExpectedAccountRevision, [property: JsonRequired] int ShareCount,
    [property: JsonRequired] IReadOnlyList<long> CustomAmountsMinor, Guid? SupersedesPlanId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePlanProofDto(
    [property: JsonRequired] int SchemaVersion, [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] Guid OperationId, [property: JsonRequired] string PayloadHash,
    [property: JsonRequired] string OwnerProof);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePlanSubmissionDto([property: JsonRequired] CustomerTablePlanIntentDto Intent,
    [property: JsonRequired] CustomerTablePlanProofDto Proof);

public sealed record AccountSharePlanReviewDto(Guid TableId, long TotalMinor, string Currency,
    int RoundingIncrementMinor, IReadOnlyList<AccountPaymentAllocationDto> Scope, IReadOnlyList<long> SharesMinor);

public sealed record CustomerTablePlanPreparedDto(int SchemaVersion, CustomerTablePlanProofDto Proof,
    Guid ServiceSessionId, long AccountRevision, long TotalMinor, string Currency,
    int RoundingIncrementMinor, IReadOnlyList<AccountPaymentAllocationDto> Scope, IReadOnlyList<long> SharesMinor);

public sealed record CustomerTablePlanResolutionDto(int SchemaVersion, Guid OperationId,
    Guid ServiceSessionId, string Outcome, Guid? PlanId);

public sealed record CustomerTablePlanBinding(Guid ServiceSessionId, Guid TableId, Guid ParticipantId,
    string ParticipantTokenHash, Guid GuestOperationId, long AccountRevision, long TotalMinor,
    string Currency, int RoundingIncrementMinor, string ScopeHash, string PlanPayloadHash);
