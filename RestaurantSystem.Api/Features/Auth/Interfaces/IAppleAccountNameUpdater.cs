using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

public interface IAppleAccountNameUpdater
{
    Task UpdateAsync(ApplicationUser user, string? firstName, string? lastName);
}
