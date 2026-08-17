using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.FloorPlan;

/// <summary>
/// GET /api/floorplan (FLOOR-PLAN-REVAMP §5.2): the anonymous guest map loads the
/// default plan — dims, walls (+ openings), items and table geometry — in one
/// payload, with wall vertices round-tripping out of the jsonb column.
/// </summary>
[Collection("Database Lane 4")]
public class GetFloorPlanTests : IntegrationTestBase
{
    private const string Endpoint = "/api/floorplan";

    public GetFloorPlanTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Get_NoPlan_Returns404()
    {
        var response = await Client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_DefaultPlan_ReturnsWholeDocument_Anonymous()
    {
        var planId = await SeedPlanAsync();

        // No auth header at all — the guest map is anonymous.
        AuthenticateAsUser();
        var response = await Client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await ReadDataAsync(response);
        doc.Id.Should().Be(planId);
        doc.WidthMeters.Should().Be(14.00m);
        doc.HeightMeters.Should().Be(9.00m);
        doc.GridSizeCm.Should().Be(25);
        doc.IsDefault.Should().BeTrue();

        // Wall + opening + vertices survive the jsonb round-trip.
        doc.Walls.Should().HaveCount(1);
        var wall = doc.Walls[0];
        wall.RoomName.Should().Be("Main room");
        wall.Points.Should().HaveCount(4);
        wall.Points[1].X.Should().Be(9.40m);
        wall.Openings.Should().ContainSingle(o => o.Kind == "door");

        // Item + table geometry present.
        doc.Items.Should().ContainSingle(i => i.Kind == "bar_counter");
        doc.Tables.Should().HaveCount(2);
        var t1 = doc.Tables.Single(t => t.TableNumber == "1");
        t1.PositionX.Should().Be(1.50m);
        t1.Shape.Should().Be("round");
        t1.MaxGuests.Should().Be(4);
    }

    private async Task<Guid> SeedPlanAsync()
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
            Walls =
            {
                new FloorPlanWall
                {
                    PointsJson = "[{\"x\":0.3,\"y\":0.3},{\"x\":9.4,\"y\":0.3},{\"x\":9.4,\"y\":8.7},{\"x\":0.3,\"y\":8.7}]",
                    ThicknessMeters = 0.18m,
                    IsClosed = true,
                    RoomName = "Main room",
                    FloorStyle = "wood",
                    CreatedBy = "seed",
                    Openings = { new FloorPlanOpening { SegmentIndex = 2, OffsetMeters = 2.2m, WidthMeters = 1.2m, Kind = "door", SwingDirection = "in", CreatedBy = "seed" } },
                },
            },
            Items =
            {
                new FloorPlanItem { Kind = "bar_counter", X = 3.1m, Y = 1.05m, WidthMeters = 3.6m, HeightMeters = 0.7m, CreatedBy = "seed" },
            },
        };
        db.FloorPlans.Add(plan);
        await db.SaveChangesAsync();

        db.Tables.AddRange(
            NewTable("1", 4, "round", 1.5m, 2.5m, plan.Id),
            NewTable("2", 2, "round", 3.5m, 2.5m, plan.Id));
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private static Table NewTable(string number, int seats, string shape, decimal x, decimal y, Guid planId) => new()
    {
        TableNumber = number,
        MaxGuests = seats,
        IsActive = true,
        Shape = shape,
        PositionX = x,
        PositionY = y,
        Width = 1.0m,
        Height = 1.0m,
        FloorPlanId = planId,
        CreatedBy = "seed",
    };

    private static async Task<FloorPlanDocumentDto> ReadDataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<FloorPlanDocumentDto>>(json, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
