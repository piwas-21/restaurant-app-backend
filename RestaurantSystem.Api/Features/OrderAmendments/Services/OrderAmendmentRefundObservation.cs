using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentRefundObservation(
    OrderAmendmentRefundEvidenceKind Kind,
    OrderAmendmentRefundLegState State,
    Guid ActorId,
    DateTime ObservedAt,
    string? TillReference,
    AmendmentRefundEvidence? Provider,
    string AuditIdentifier);
