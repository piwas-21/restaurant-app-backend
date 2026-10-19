using Microsoft.Net.Http.Headers;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

internal static class CustomerBasketAccess
{
    public static void EnsureCustomerOrGuest(ICurrentUserService currentUser, HttpContext? httpContext)
    {
        if (currentUser.IsApiToken || currentUser.IsAuthenticated && currentUser.Role != UserRole.Customer)
            throw new ForbiddenException("Customer baskets require a customer or guest session.");
        if (!currentUser.IsAuthenticated && httpContext?.Request.Headers.ContainsKey(HeaderNames.Authorization) == true)
            throw new UnauthorizedAccessException("Renew customer authentication before using this basket.");
    }
}
