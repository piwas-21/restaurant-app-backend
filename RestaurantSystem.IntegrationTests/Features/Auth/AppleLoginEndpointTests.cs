using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// POST /api/Auth/apple-login, end to end (BACKEND-NOTES §4.1 and §4.2).
///
/// <para>
/// §4.1 is the takeover: an unsigned JWT naming an existing account's address used to return that
/// account's access + refresh token. The first test posts exactly that against a password
/// account and demands a refusal WITH no tokens in the body — a status-code-only assertion would
/// still pass if the handler leaked a session alongside a failure flag.
/// </para>
/// <para>
/// §4.2 is the name: Apple releases <c>fullName</c> only on an Apple ID's first authorisation, so
/// an account created without one was stuck as "Apple User" forever.
/// </para>
///
/// Apple itself is replaced at the <see cref="IAppleSigningKeyProvider"/> seam, so these run
/// offline. Overriding <c>ConfigureTestServices</c> also gives this class its own host, which
/// keeps the per-IP "auth" rate limit (3/min in test config) out of the way.
/// </summary>
[Collection("Database Lane 2")]
public class AppleLoginEndpointTests : IntegrationTestBase
{
    private const string Password = "Str0ng!Passw0rd"; // pragma: allowlist secret (test-only)

    public AppleLoginEndpointTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IAppleSigningKeyProvider>();
        services.AddSingleton<IAppleSigningKeyProvider>(
            new FakeAppleSigningKeyProvider(new[] { AppleTestTokens.PublicKey }));

        // Applied last, so it wins over the (empty) Authentication:Apple section.
        services.Configure<AppleAuthSettings>(options =>
        {
            options.ClientIds = new List<string> { AppleTestTokens.ClientId };
            options.Issuer = AppleTestTokens.Issuer;
        });
    }

    /// <summary>The reported hole, at the HTTP boundary.</summary>
    [Fact]
    public async Task AppleLogin_WithAnUnsignedToken_CannotTakeOverAPasswordAccount()
    {
        var email = "password-account@example.test";
        await SeedUserAsync(email, "Real", "Person", withPassword: true);

        var response = await PostAsync(AppleTestTokens.Unsigned(email));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.InvalidAppleToken);
        body.Data.Should().BeNull("a refused login must not hand out an access or refresh token");
    }

    [Fact]
    public async Task AppleLogin_WithAValidToken_SignsInAndCreatesTheAccount()
    {
        var email = "new-apple-user@example.test";

        var response = await PostAsync(AppleTestTokens.Valid(email), "Ada", "Lovelace");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body!.Success.Should().BeTrue(string.Join(", ", body.Errors ?? new List<string>()));
        body.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.Data.FirstName.Should().Be("Ada");
        body.Data.LastName.Should().Be("Lovelace");

        var stored = await FindUserAsync(email);
        stored!.FirstName.Should().Be("Ada");
    }

    /// <summary>
    /// Apple gave no name — every login after the first — so the placeholder stands. It is not
    /// left empty because an empty name fails UpdateUserProfileCommandValidator, which would stop
    /// the user saving any other profile field.
    /// </summary>
    [Fact]
    public async Task AppleLogin_WithoutAName_CreatesThePlaceholderAccount()
    {
        var email = "nameless-apple-user@example.test";

        var response = await PostAsync(AppleTestTokens.Valid(email));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await FindUserAsync(email);
        stored!.FirstName.Should().Be("Apple");
        stored.LastName.Should().Be("User");
    }

    /// <summary>§4.2: the incoming non-empty name wins over what is stored.</summary>
    [Fact]
    public async Task AppleLogin_WithAName_RefreshesTheStoredPlaceholder()
    {
        var email = "stuck-as-apple-user@example.test";
        await SeedUserAsync(email, "Apple", "User", withPassword: false);

        var response = await PostAsync(AppleTestTokens.Valid(email), "Grace", "Hopper");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body!.Data!.FirstName.Should().Be("Grace", "the response must show the refreshed name too");

        var stored = await FindUserAsync(email);
        stored!.FirstName.Should().Be("Grace");
        stored.LastName.Should().Be("Hopper");
    }

    /// <summary>The other half of §4.2: a silent login must not wipe a name the user chose.</summary>
    [Fact]
    public async Task AppleLogin_WithoutAName_KeepsTheStoredName()
    {
        var email = "already-named@example.test";
        await SeedUserAsync(email, "Katherine", "Johnson", withPassword: false);

        var response = await PostAsync(AppleTestTokens.Valid(email));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await FindUserAsync(email);
        stored!.FirstName.Should().Be("Katherine");
        stored.LastName.Should().Be("Johnson");
    }

    [Fact]
    public async Task AppleLogin_WithAnEmptyToken_IsRefusedByValidation()
    {
        var response = await PostAsync(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<HttpResponseMessage> PostAsync(string idToken, string? firstName = null, string? lastName = null)
    {
        AuthenticateAsAnonymous();
        return Client.PostAsJsonAsync("/api/Auth/apple-login", new
        {
            idToken,
            firstName,
            lastName,
        });
    }

    private static async Task<ApiResponse<AuthResponse>?> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>(JsonOptions);

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    private async Task SeedUserAsync(string email, string firstName, string lastName, bool withPassword)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Customer,
            CreatedBy = "test",
            RefreshToken = string.Empty,
        };

        var created = withPassword
            ? await userManager.CreateAsync(user, Password)
            : await userManager.CreateAsync(user);

        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
    }
}
