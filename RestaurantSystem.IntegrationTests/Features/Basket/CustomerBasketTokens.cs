using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

internal static class CustomerBasketTokens
{
    public static string Create(IServiceProvider services, UserRole role, bool expired = false, string? userId = null)
    {
        var settings = services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(ClaimTypes.NameIdentifier, userId ?? TestAuthHandler.UserId), new Claim(ClaimTypes.Role, role.ToString())],
            now.AddHours(-2), now.AddMinutes(expired ? -30 : 30), settings.SigningCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
