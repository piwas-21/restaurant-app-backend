namespace RestaurantSystem.Api.Common.Exceptions;

/// <summary>A pre-debit balance refusal; no fidelity ledger or balance row was changed.</summary>
internal sealed class InsufficientPointsException : BadRequestException
{
    internal InsufficientPointsException(int available, int requested)
        : base($"Insufficient points. Available: {available}, Requested: {requested}")
    {
    }
}
