namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableIdentityResolver
{
    Task<TableIdentity> ResolveActiveAsync(
        Guid? tableId,
        int? tableNumber,
        CancellationToken cancellationToken);
}

public sealed record TableIdentity(Guid Id, string Label, int? Number);
