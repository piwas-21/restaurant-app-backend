using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

public interface IRefreshSessionService
{
    Task<string> IssueAsync(ApplicationUser user, CancellationToken cancellationToken);
    Task<string?> RotateAsync(ApplicationUser user, string refreshToken, CancellationToken cancellationToken);
    Task RevokeAllAsync(ApplicationUser user, CancellationToken cancellationToken);
}
