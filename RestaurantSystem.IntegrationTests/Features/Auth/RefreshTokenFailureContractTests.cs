using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 1")]
public class RefreshTokenFailureContractTests : IntegrationTestBase
{
    private const string InternalFailure = "sensitive database fixture detail";

    public RefreshTokenFailureContractTests(DatabaseFixture fixture) : base(fixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var sessions = new Mock<IRefreshSessionService>();
        sessions.Setup(service => service.RotateAsync(
                It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser user, string token, CancellationToken cancellation) => token switch
            {
                "unavailable-session" => throw new InvalidOperationException(InternalFailure),
                "valid-session" => "rotated-session",
                _ => null
            });
        services.RemoveAll<IRefreshSessionService>();
        services.AddScoped(_ => sessions.Object);
    }

    [Fact]
    public async Task Rejected_access_signature_is_a_terminal_401()
    {
        using var response = await Client.PostAsJsonAsync("/api/Auth/refresh-token",
            new { accessToken = "invalid-signature", refreshToken = "stale-session" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejected_refresh_credential_is_a_terminal_401()
    {
        using var response = await RefreshAsync("spent-session");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unexpected_storage_failure_is_500_without_exposing_internal_details()
    {
        using var response = await RefreshAsync("unavailable-session");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(InternalFailure);
    }

    [Fact]
    public async Task Successful_refresh_preserves_the_200_envelope()
    {
        using var response = await RefreshAsync("valid-session");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("data").GetProperty("refreshToken").GetString().Should().Be("rotated-session");
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        return await Client.PostAsJsonAsync("/api/Auth/refresh-token",
            new { accessToken = tokens.GenerateAccessToken(user), refreshToken });
    }
}
