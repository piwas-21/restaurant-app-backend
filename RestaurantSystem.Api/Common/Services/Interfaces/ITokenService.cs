using RestaurantSystem.Domain.Entities;
using System.Security.Claims;

namespace RestaurantSystem.Api.Common.Services.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(ApplicationUser user);
        string GenerateRefreshToken();
        string HashRefreshToken(string token);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
        DateTime GetAccessTokenExpiration();
    }
}
