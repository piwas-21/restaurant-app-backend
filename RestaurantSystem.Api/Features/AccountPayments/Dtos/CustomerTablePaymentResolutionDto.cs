using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public enum CustomerTablePaymentOutcome { Unseen, Stopped, Applied }

/// <summary>Applied means the original reservation exists; it is never settlement proof.</summary>
public sealed record CustomerTablePaymentResolutionDto(int SchemaVersion, Guid OperationId,
    Guid ServiceSessionId, CustomerTablePaymentOutcome Outcome, Guid? AttemptId, AccountPaymentState? State);
