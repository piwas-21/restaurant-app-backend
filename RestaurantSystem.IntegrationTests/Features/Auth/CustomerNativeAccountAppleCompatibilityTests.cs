using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountAppleCompatibilityTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    private readonly RefusedName _refusal = new(fixture);
    private readonly Mock<IAppleIdentityTokenVerifier> _apple = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<IUserValidator<ApplicationUser>>(_refusal);
        services.RemoveAll<IAppleIdentityTokenVerifier>();
        services.AddSingleton(_apple.Object);
    }

    [Theory]
    [InlineData("cosmetic")]
    [InlineData("stamp")]
    [InlineData("role")]
    public async Task A_failed_name_update_preserves_signin_only_when_its_original_authority_is_still_current(string change)
    {
        var user = await CustomerAsync(confirmed: true);
        _apple.Setup(value => value.ValidateAsync("verified-apple-fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppleTokenValidationResult.Valid(new("verified-subject", user.Email, true, null)));
        _refusal.Change = change;
        _refusal.Armed = true;
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync("/api/Auth/apple-login", new
        {
            idToken = "verified-apple-fixture",
            firstName = "Attempted",
            lastName = "Name"
        });
        _refusal.Triggered.Should().BeTrue();
        var stored = await ReadUserAsync(user.Id);
        stored.FirstName.Should().Be("Native");
        stored.LastName.Should().Be("Customer");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (change == "cosmetic")
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>(JsonOptions))!;
            body.Success.Should().BeTrue();
            body.Data!.FirstName.Should().Be("Native");
            body.Data.LastName.Should().Be("Customer");
            body.Data.AccessToken.Should().NotBeNullOrWhiteSpace();
            (await context.RefreshSessions.CountAsync(row => row.UserId == user.Id)).Should().Be(1);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().NotContain("accessToken").And.NotContain("refreshToken");
            (await context.RefreshSessions.CountAsync(row => row.UserId == user.Id)).Should().Be(0);
        }
    }

    private sealed class RefusedName(DatabaseFixture fixture) : IUserValidator<ApplicationUser>
    {
        public string Change { get; set; } = "cosmetic";
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            if (!Armed) return IdentityResult.Success;
            Armed = false;
            Triggered = true;
            if (Change != "cosmetic")
            {
                // Independent database writer: never manufacture the race through the updater.
                await using var context = fixture.CreateContext();
                var current = await context.Users.SingleAsync(row => row.Id == user.Id);
                if (Change == "stamp") current.SecurityStamp = Guid.NewGuid().ToString("N");
                if (Change == "role") current.Role = UserRole.Admin;
                current.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await context.SaveChangesAsync();
            }
            return IdentityResult.Failed(new IdentityError { Code = "NameUpdateRefused", Description = "Cosmetic update refused" });
        }
    }
}
