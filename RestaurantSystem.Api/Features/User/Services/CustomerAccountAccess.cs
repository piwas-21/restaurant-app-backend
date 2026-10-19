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
}
