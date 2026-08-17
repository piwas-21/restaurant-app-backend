using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// POST /api/tables after the FLOOR-PLAN-REVAMP metre reinterpretation
/// (§5.2/§6). The legacy create path now auto-links the new table to the
/// default plan (so it appears on the guest map) and coerces its geometry
/// into metres — honouring metric input, discarding pixel-era input for a
/// seats-derived footprint, recentring out-of-bounds positions, and mapping
/// the legacy "circle" shape to "round". The wire ranges still admit the
/// still-deployed pixel frontend so it does not start 400-ing (prod-first).
/// </summary>
[Collection("Database Lane 3")]
public class CreateTableTests : IntegrationTestBase
{
    private const string Endpoint = "/api/tables";
    private const decimal PlanWidth = 12m;
    private const decimal PlanHeight = 10m;

    public CreateTableTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Post_LegacyPixelPayload_CoercesToMetresAndAutoLinks()
    {
        // Exactly what the still-deployed CreateTableModal sends: an 80×80 px
        // "circle" 4-top centred on the old 600×500 canvas.
        var planId = await SeedDefaultPlanAsync();
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "P1",
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 260m,
            positionY = 210m,
            width = 80m,
            height = 80m,
            shape = "circle",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await ReadDataAsync<TableDto>(response);
        dto.Width.Should().Be(1.20m);   // seats-derived (4-top), not 80
        dto.Height.Should().Be(0.80m);
        dto.Shape.Should().Be("round"); // "circle" normalised
        dto.PositionX.Should().Be(PlanWidth / 2m);  // pixel coords recentred
        dto.PositionY.Should().Be(PlanHeight / 2m);

        var stored = await LoadTableAsync("P1");
        stored.FloorPlanId.Should().Be(planId);
    }

    [Fact]
    public async Task Post_MetricPayload_IsStoredAsSuppliedAndAutoLinked()
    {
        var planId = await SeedDefaultPlanAsync();
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "M1",
            maxGuests = 6,
            isActive = true,
            isOutdoor = false,
            positionX = 6.0m,
            positionY = 4.0m,
            width = 1.5m,
            height = 0.9m,
            shape = "square",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var stored = await LoadTableAsync("M1");
        stored.Width.Should().Be(1.5m);
        stored.Height.Should().Be(0.9m);
        stored.PositionX.Should().Be(6.0m);
        stored.PositionY.Should().Be(4.0m);
        stored.Shape.Should().Be("square");
        stored.FloorPlanId.Should().Be(planId);
    }

    [Fact]
    public async Task Post_OmittedGeometry_DerivesSeatsFootprintAndCentres()
    {
        var planId = await SeedDefaultPlanAsync();
        AuthenticateAsAdmin();

        // The metre editor may create with no geometry and place via the PUT.
        var response = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "O1",
            maxGuests = 2,
            isActive = true,
            isOutdoor = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var stored = await LoadTableAsync("O1");
        stored.Width.Should().Be(0.70m);   // 2-top
        stored.Height.Should().Be(0.70m);
        stored.PositionX.Should().Be(PlanWidth / 2m);
        stored.PositionY.Should().Be(PlanHeight / 2m);
        stored.Shape.Should().Be("round"); // DTO default
        stored.FloorPlanId.Should().Be(planId);
    }

    [Fact]
    public async Task Post_CreatedTable_AppearsOnTheGuestMap()
    {
        // End-to-end proof of auto-link: a table created via /api/tables is
        // returned by the anonymous GET /api/floorplan the guest map renders.
        await SeedDefaultPlanAsync();
        AuthenticateAsAdmin();

        var create = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "G9",
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        AuthenticateAsUser();
        var map = await Client.GetAsync("/api/floorplan");
        map.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await ReadDataAsync<FloorPlanDocumentDto>(map);
        doc.Tables.Should().ContainSingle(t => t.TableNumber == "G9");
    }

    [Fact]
    public async Task Post_NoPlanExists_CreatesUnlinkedButStillMetric()
    {
        // Defensive: without a plan the table degrades to unlinked, but its
        // geometry is still sane metres (never the pixel input).
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "U1",
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            width = 80m,
            height = 80m,
            shape = "circle",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var stored = await LoadTableAsync("U1");
        stored.FloorPlanId.Should().BeNull();
        stored.Width.Should().Be(1.20m);
        stored.Shape.Should().Be("round");
    }

    [Fact]
    public async Task Post_SizeAboveWireRange_Returns400()
    {
        // 600 is past the [Range(0.1, 500)] ceiling that admits the legacy
        // pixel marker — a genuinely out-of-contract size is still rejected.
        await SeedDefaultPlanAsync();
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync(Endpoint, new
        {
            tableNumber = "B1",
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            width = 600m,
            height = 80m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Width");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedDefaultPlanAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plan = new Domain.Entities.FloorPlan
        {
            Name = "Main floor",
            WidthMeters = PlanWidth,
            HeightMeters = PlanHeight,
            GridSizeCm = 25,
            IsDefault = true,
            CreatedBy = "seed",
        };
        db.FloorPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Table> LoadTableAsync(string tableNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var table = await db.Tables.AsNoTracking().SingleOrDefaultAsync(t => t.TableNumber == tableNumber);
        table.Should().NotBeNull();
        return table!;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
