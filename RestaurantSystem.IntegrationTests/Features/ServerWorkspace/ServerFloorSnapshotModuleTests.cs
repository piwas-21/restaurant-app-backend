using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.ServerWorkspace;

[Collection("Database Lane 1")]
public sealed class ServerFloorSnapshotModuleTests
{
    private readonly DatabaseFixture _fixture;

    public ServerFloorSnapshotModuleTests(DatabaseFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("core,server", HttpStatusCode.OK)]
    [InlineData("core,cashier", HttpStatusCode.OK)]
    [InlineData("core,server,cashier", HttpStatusCode.OK)]
    [InlineData("core", HttpStatusCode.NotFound)]
    [InlineData("", HttpStatusCode.OK)]
    public async Task Snapshot_obeys_the_server_or_cashier_module_matrix(
        string enabledModules, HttpStatusCode expected)
    {
        await _fixture.ResetDatabaseAsync();
        using var factory = new TestWebApplicationFactory(
            _fixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = enabledModules
            });
        using (var scope = factory.Services.CreateScope())
        {
            await TestDataSeeder.SeedBasicDataAsync(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin", "true");

        var response = await client.GetAsync("/api/staff/server-workspace/floor");
        response.StatusCode.Should().Be(expected);
    }
}
