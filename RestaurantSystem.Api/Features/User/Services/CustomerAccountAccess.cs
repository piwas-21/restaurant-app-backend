using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.User.Services;

public static class CustomerAccountAccess
{
    public static Guid RequireOwner(ICurrentUserService currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } owner)
            throw new UnauthorizedAccessException("Customer authentication is required.");
        if (currentUser.IsApiToken || currentUser.Role != UserRole.Customer)
            throw new ForbiddenException("This account action requires its customer.");
        return owner;
    }
    public static IQueryable<ApplicationUser> Current(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        var owner = RequireOwner(currentUser);
        return context.Users.Where(user => user.Id == owner && !user.IsDeleted && user.Role == UserRole.Customer);
    }
}
