using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// fix/auth-refresh-rate-limit: refresh-token expiry must honor the configurable
/// <see cref="JwtSettings.RefreshTokenExpiryDays"/> via
/// <c>TokenService.GetRefreshTokenExpiration()</c>. Previously every login/register
/// handler hardcoded <c>DateTime.UtcNow.AddDays(7)</c>, silently ignoring the setting.
/// </summary>
public class TokenServiceExpiryTests
{
    private static TokenService CreateTokenService(int refreshDays) =>
        new(Options.Create(new JwtSettings
        {
            Secret = new string('k', 64),
            Issuer = "test-issuer",
            Audience = "test-audience",
            RefreshTokenExpiryDays = refreshDays,
        }));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public void GetRefreshTokenExpiration_HonorsConfiguredDays(int days)
    {
        var expected = DateTime.UtcNow.AddDays(days);

        var actual = CreateTokenService(days).GetRefreshTokenExpiration();

        actual.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }
}
