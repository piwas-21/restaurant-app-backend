namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Resolves the safe legacy target for a table-number bill.</summary>
public interface ITableBillTargetResolver
{
    Task<TableBillTarget> ResolveAsync(int tableNumber, CancellationToken cancellationToken);
}

public sealed record TableBillTarget(
    Guid? ServiceSessionId,
    bool IsAmbiguous,
    string? Reason = null);
