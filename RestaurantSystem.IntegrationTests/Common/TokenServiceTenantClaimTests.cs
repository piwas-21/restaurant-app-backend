using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Issue #117 (saas-prep): access tokens carry a <c>tenant</c> claim when
/// <see cref="JwtSettings.TenantSlug"/> is configured (instance-per-tenant,
/// sofra ADR-001 / backend ADR-003 amendment) and stay byte-compatible with
/// the legacy claim set when it is not — the RUMI install must be unaffected
/// until provisioning injects <c>JwtSettings__TenantSlug</c>.
/// </summary>
public class TokenServiceTenantClaimTests
{
    private static TokenService CreateTokenService(string tenantSlug)
    {
        var settings = new JwtSettings
        {
            Secret = new string('k', 64),
            Issuer = "test-issuer",
            Audience = "test-audience",
            TenantSlug = tenantSlug,
        };
        return new TokenService(Options.Create(settings));
    }

    private static ApplicationUser CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@example.test",
        FirstName = "Test",
        LastName = "User",
        Role = UserRole.Customer,
        CreatedBy = "test",
        RefreshToken = string.Empty,
    };

    [Fact]
    public void GenerateAccessToken_WithTenantSlugConfigured_EmitsTenantClaim()
    {
        var token = CreateTokenService("demo").GenerateAccessToken(CreateUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().ContainSingle(c => c.Type == TokenService.TenantClaimType)
            .Which.Value.Should().Be("demo");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateAccessToken_WithoutTenantSlug_OmitsTenantClaim(string tenantSlug)
    {
        var token = CreateTokenService(tenantSlug).GenerateAccessToken(CreateUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == TokenService.TenantClaimType);
    }
}
