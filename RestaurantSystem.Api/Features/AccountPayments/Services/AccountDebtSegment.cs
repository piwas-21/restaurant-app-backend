namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>A compact one-based range with an exact, uniform amount due for each unit.</summary>
internal sealed record AccountDebtSegment(
    Guid OrderId, Guid? OrderItemId, int StartOrdinal, int Count, long MinorPerUnit)
{
    internal long EndExclusive => (long)StartOrdinal + Count;
    internal long TotalMinor => checked(MinorPerUnit * Count);
}

internal sealed record AccountUnitIdentity(Guid OrderId, Guid OrderItemId, int Ordinal);
