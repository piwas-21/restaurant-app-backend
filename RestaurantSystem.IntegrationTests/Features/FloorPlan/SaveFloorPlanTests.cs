using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.FloorPlan;

/// <summary>
/// PUT /api/floorplan/{id} (FLOOR-PLAN-REVAMP §5.2): admin saves the whole
/// document — walls/openings/items replaced wholesale, table geometry applied by
/// id (unknown ids ignored) — under optimistic concurrency on UpdatedAt and
/// Admin-only authorization.
/// </summary>
[Collection("Database Lane 1")]
public class SaveFloorPlanTests : IntegrationTestBase
{
    private const string Endpoint = "/api/floorplan";

    public SaveFloorPlanTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Put_AsAdmin_ReplacesGeometryAndAppliesTablePosition()
    {
        var (planId, tableId) = await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();

        var doc = await GetDocumentAsync();
        doc.Walls = new List<FloorPlanWallDto>
        {
            new()
            {
                Points = new List<FloorPlanPointDto> { new() { X = 1m, Y = 1m }, new() { X = 5m, Y = 1m }, new() { X = 5m, Y = 4m } },
                IsClosed = true,
                RoomName = "Salon",
                FloorStyle = "tile",
                Openings = new List<FloorPlanOpeningDto> { new() { SegmentIndex = 0, OffsetMeters = 1m, WidthMeters = 0.9m, Kind = "window" } },
            },
        };
        doc.Items = new List<FloorPlanItemDto>
        {
            new() { Kind = "piano", X = 2m, Y = 2m, WidthMeters = 1.5m, HeightMeters = 2m, RotationDegrees = 18m },
        };
        doc.Tables = new List<FloorPlanTableGeometryDto>
        {
            new() { Id = tableId, TableNumber = "1", MaxGuests = 4, Shape = "square", PositionX = 6.25m, PositionY = 3.5m, Width = 0.92m, Height = 0.92m, Rotation = 90 },
        };

        var response = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var saved = await ReadDataAsync(response);
        saved.Walls.Should().ContainSingle(w => w.RoomName == "Salon");
        saved.Items.Should().ContainSingle(i => i.Kind == "piano");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var table = await db.Tables.AsNoTracking().SingleAsync(t => t.Id == tableId);
        table.PositionX.Should().Be(6.25m);
        table.PositionY.Should().Be(3.50m);
        table.Shape.Should().Be("square");
        table.Rotation.Should().Be(90);
        table.FloorPlanId.Should().Be(planId);

        // The old wall/item are gone, not accumulated.
        (await db.FloorPlanWalls.CountAsync(w => w.FloorPlanId == planId)).Should().Be(1);
        (await db.FloorPlanItems.CountAsync(i => i.FloorPlanId == planId)).Should().Be(1);
    }

    [Fact]
    public async Task Put_StaleUpdatedAt_Returns409()
    {
        var (planId, _) = await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();

        var doc = await GetDocumentAsync();          // UpdatedAt == null (pristine)
        var firstSave = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);

        // Reuse the now-stale token — the first save advanced UpdatedAt.
        var second = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_UnknownPlanId_Returns404()
    {
        await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();
        var doc = await GetDocumentAsync();

        var response = await PutAsJsonAsync($"{Endpoint}/{Guid.NewGuid()}", doc);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AsNonAdmin_Returns403()
    {
        var (planId, _) = await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();
        var doc = await GetDocumentAsync();

        AuthenticateAsUser();  // authenticated Customer, not Admin
        var response = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_UnknownTableId_IsIgnored_LeavesRealTableUntouched()
    {
        var (planId, tableId) = await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();

        var doc = await GetDocumentAsync();
        doc.Tables = new List<FloorPlanTableGeometryDto>
        {
            new() { Id = Guid.NewGuid(), TableNumber = "ghost", MaxGuests = 2, Shape = "round", PositionX = 2m, PositionY = 2m, Width = 0.7m, Height = 0.7m },
        };

        var response = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // No phantom row created, and the real table keeps its seeded position.
        (await db.Tables.CountAsync()).Should().Be(1);
        var table = await db.Tables.AsNoTracking().SingleAsync(t => t.Id == tableId);
        table.PositionX.Should().Be(1.50m);
    }

    [Theory]
    [InlineData("width", "0")]
    [InlineData("itemkind", "not_a_real_kind")]
    [InlineData("shape", "triangle")]
    public async Task Put_InvalidDocument_Returns400(string scenario, string _)
    {
        var (planId, tableId) = await SeedPlanWithTableAsync();
        AuthenticateAsAdmin();
        var doc = await GetDocumentAsync();

        switch (scenario)
        {
            case "width":
                doc.WidthMeters = 0m;   // below InclusiveBetween(1, 100)
                break;
            case "itemkind":
                doc.Items = new List<FloorPlanItemDto> { new() { Kind = "not_a_real_kind", X = 1m, Y = 1m, WidthMeters = 1m, HeightMeters = 1m } };
                break;
            case "shape":
                doc.Tables = new List<FloorPlanTableGeometryDto>
                {
                    new() { Id = tableId, TableNumber = "1", MaxGuests = 4, Shape = "triangle", PositionX = 1m, PositionY = 1m, Width = 1m, Height = 1m },
                };
                break;
        }

        var response = await PutAsJsonAsync($"{Endpoint}/{planId}", doc);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<(Guid PlanId, Guid TableId)> SeedPlanWithTableAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plan = new Domain.Entities.FloorPlan
        {
            Name = "Main floor",
            WidthMeters = 14m,
            HeightMeters = 9m,
            GridSizeCm = 25,
            IsDefault = true,
            CreatedBy = "seed",
            Walls = { new FloorPlanWall { PointsJson = "[{\"x\":0.3,\"y\":0.3},{\"x\":9.4,\"y\":0.3}]", CreatedBy = "seed" } },
            Items = { new FloorPlanItem { Kind = "fireplace", X = 1m, Y = 4m, WidthMeters = 1.5m, HeightMeters = 1m, CreatedBy = "seed" } },
        };
        db.FloorPlans.Add(plan);
        await db.SaveChangesAsync();

        var table = new Table
        {
            TableNumber = "1",
            MaxGuests = 4,
            IsActive = true,
            Shape = "round",
            PositionX = 1.5m,
            PositionY = 2.5m,
            Width = 1.0m,
            Height = 1.0m,
            FloorPlanId = plan.Id,
            CreatedBy = "seed",
        };
        db.Tables.Add(table);
        await db.SaveChangesAsync();
        return (plan.Id, table.Id);
    }

    private async Task<FloorPlanDocumentDto> GetDocumentAsync()
    {
        var response = await Client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadDataAsync(response);
    }

    private static async Task<FloorPlanDocumentDto> ReadDataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<FloorPlanDocumentDto>>(json, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
