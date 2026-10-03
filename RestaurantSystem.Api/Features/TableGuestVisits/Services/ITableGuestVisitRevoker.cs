namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

/// <summary>Revokes visit-scoped guest credentials inside the caller's close transaction.</summary>
public interface ITableGuestVisitRevoker
{
    Task RevokeForSessionAsync(Guid serviceSessionId, DateTime revokedAt, CancellationToken cancellationToken);
}
