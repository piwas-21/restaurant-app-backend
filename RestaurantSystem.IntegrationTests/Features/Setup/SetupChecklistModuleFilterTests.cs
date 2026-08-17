using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Setup;
using RestaurantSystem.Api.Features.Setup.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Setup;

/// <summary>
/// The checklist only offers steps the tenant's modules entitle them to
/// (SOFRA-ONBOARDING-PLAN O4, on top of O5's enforcement).
///
/// A step pointing at a module the tenant did not buy sends an owner to a surface that
/// 404s under <c>RequireModule</c> and whose nav entry the frontend has already hidden
/// — the "gate everything that links to a route" rule, applied to guidance rather than
/// chrome. Telling somebody to go somewhere that does not exist is worse than saying
/// nothing.
///
/// Enforcement is switched ON here, which is the only configuration in which the filter
/// is observable: with it off, <c>ITenantModules</c> reports the whole vocabulary and
/// every step is entitled — so a test without this would pass against a filter that
/// does nothing at all.
/// </summary>
[Collection("Database Lane 2")]
public class SetupChecklistModuleFilterTests : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public SetupChecklistModuleFilterTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public async Task InitializeAsync()
    {
        // A reservations-and-loyalty tenant with no printing — so one module-owned step
        // must appear, another must not, and the difference is not "all or nothing".
        _factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "core,reservations,loyalty",
            });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-Admin", "true");

        await _databaseFixture.ResetDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        await TestDataSeeder.SeedBasicDataAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task OnlyStepsForModulesTheTenantBought()
    {
        var response = await _client.GetAsync("/api/admin/setup-checklist");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var checklist = JsonSerializer
            .Deserialize<ApiResponse<SetupChecklistDto>>(
                await response.Content.ReadAsStringAsync(), JsonOptions)!.Data!;

        var keys = checklist.Steps.Select(s => s.Key).ToList();

        keys.Should().Contain(SetupSteps.Reservations);
        keys.Should().Contain(SetupSteps.Loyalty);
        keys.Should().NotContain(SetupSteps.Printing);
        keys.Should().NotContain(SetupSteps.Cashier);
        keys.Should().NotContain(SetupSteps.KitchenBoard);
        keys.Should().NotContain(SetupSteps.Server);

        // Core steps are never module-gated — a Core-only tenant needs the guidance most.
        keys.Should().Contain(SetupSteps.RestaurantInfo);
        keys.Should().Contain(SetupSteps.Menu);
        keys.Should().Contain(SetupSteps.Staff);
    }

    [Fact]
    public async Task AStepTheTenantIsNotEntitledToIsRefusedOnWrite()
    {
        // Filtering only on READ is not enough, and the failure it leaves is a quiet
        // one. A stored acknowledgement for an unbought module is invisible while the
        // module is unbought — and then, the day the tenant upgrades and `printing`
        // finally appears on their checklist, it appears already ticked. They are never
        // walked through setting up the thing they just paid for, and nothing anywhere
        // reads as broken. So the write is refused outright.
        var ack = await _client.PutAsJsonAsync(
            $"/api/admin/setup-checklist/steps/{SetupSteps.Printing}",
            new SetStepDoneRequest { IsDone = true });
        ack.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await context.SetupChecklistState.FirstOrDefaultAsync();
        (state?.AcknowledgedSteps ?? []).Should().NotContain(SetupSteps.Printing);
    }

    [Fact]
    public async Task DoneCountIgnoresAcknowledgementsForUnboughtModules()
    {
        // The count drives a progress indicator, so counting ACKNOWLEDGEMENTS rather
        // than offered-and-done steps would let a stale key from an unbought module
        // inflate it and show an owner "6 of 5 done".
        //
        // The stale row is written through the DbContext because the API refuses that
        // write by design (AStepTheTenantIsNotEntitledToIsRefusedOnWrite). It is still
        // reachable in the real world: a tenant who DOWNGRADES keeps whatever they
        // acknowledged while they still had the module.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.SetupChecklistState.Add(new SetupChecklistState
            {
                Id = SetupChecklistState.SingletonId,
                CreatedBy = "test",
                AcknowledgedSteps = [SetupSteps.Printing, SetupSteps.OpeningHours],
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/admin/setup-checklist");
        var checklist = JsonSerializer
            .Deserialize<ApiResponse<SetupChecklistDto>>(
                await response.Content.ReadAsStringAsync(), JsonOptions)!.Data!;

        // `opening-hours` is offered and acknowledged, so it counts. `printing` is
        // acknowledged but not offered, so it must not — neither as a step nor a count.
        checklist.Steps.Should().Contain(s => s.Key == SetupSteps.OpeningHours && s.IsDone);
        checklist.Steps.Should().NotContain(s => s.Key == SetupSteps.Printing);
        checklist.DoneCount.Should().Be(checklist.Steps.Count(s => s.IsDone));
    }
}
