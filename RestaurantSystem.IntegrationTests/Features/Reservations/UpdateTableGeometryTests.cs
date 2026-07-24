using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// Regression guard for PUT /api/tables/{id}: the geometry fields
/// (width/height/shape/rotation) are optional on the wire. The admin
/// table-layout editor stopped sending them, and the handler used to assign
/// them unconditionally from a DTO whose non-nullable defaults were
/// 0/0/"circle"/0 — so an omitted shape/rotation was silently written over
/// the stored value, and an omitted width/height failed [Range(10, 500)] and
/// rejected the whole save. Those columns are not being dropped
/// (FLOOR-PLAN-REVAMP slice S1 migrates the stored values to metres), so an
/// omitted field must be a no-op, while a supplied one must still update —
/// including when it is explicitly zero — and still be range-checked.
/// </summary>
public class UpdateTableGeometryTests : IntegrationTestBase
{
    private const string Endpoint = "/api/tables";
    private const string TableNumber = "G1";

    // Deliberately unlike the DTO's old defaults (0 / 0 / "circle" / 0) so a
    // regression cannot pass by coincidence.
    private const decimal SeededWidth = 137.50m;
    private const decimal SeededHeight = 94.25m;
    private const string SeededShape = "rectangle";
    private const int SeededRotation = 45;

    public UpdateTableGeometryTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Put_OmittingGeometryFields_LeavesStoredGeometryUnchanged()
    {
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        // Exactly the payload the post-B1 admin editor sends: no width,
        // no height, no shape, no rotation.
        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 6,
            isActive = true,
            isOutdoor = false,
            positionX = 210.00m,
            positionY = 340.00m,
            notes = "window side",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The response DTO must echo the stored geometry, not the DTO defaults.
        var dto = await ReadDataAsync<TableDto>(response);
        dto.Width.Should().Be(SeededWidth);
        dto.Height.Should().Be(SeededHeight);
        dto.Shape.Should().Be(SeededShape);
        dto.Rotation.Should().Be(SeededRotation);

        // The non-geometry fields still round-trip normally.
        dto.MaxGuests.Should().Be(6);
        dto.PositionX.Should().Be(210.00m);
        dto.Notes.Should().Be("window side");

        var stored = await LoadTableAsync(tableId);
        stored.Width.Should().Be(SeededWidth);
        stored.Height.Should().Be(SeededHeight);
        stored.Shape.Should().Be(SeededShape);
        stored.Rotation.Should().Be(SeededRotation);
    }

    [Fact]
    public async Task Put_OmittingOnlyShapeAndRotation_LeavesThemUnchanged()
    {
        // The silent-data-loss path specifically: width/height are supplied so
        // [Range(10, 500)] is satisfied and the request reaches the handler,
        // while the omitted shape/rotation defaulted to "circle"/0 — values
        // that pass validation and were then written straight over the stored
        // ones. No error, no 400, just quietly flattened geometry.
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            width = SeededWidth,
            height = SeededHeight,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await LoadTableAsync(tableId);
        stored.Shape.Should().Be(SeededShape);
        stored.Rotation.Should().Be(SeededRotation);

        // Positive control: every other field in the payload matches the seed,
        // so without this the test would also pass against a handler that
        // wrote nothing at all.
        stored.PositionX.Should().Be(10.00m);
    }

    [Fact]
    public async Task Put_ExplicitZeroRotation_IsWritten()
    {
        // Zero is a legitimate rotation, so the guard has to test presence and
        // not truthiness. Pins the difference: swapping HasValue for an
        // `is > 0`-style check would make resetting a table to 0° impossible.
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            rotation = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await LoadTableAsync(tableId);
        stored.Rotation.Should().Be(0);
        stored.Shape.Should().Be(SeededShape);  // still omitted → still untouched
    }

    [Fact]
    public async Task Put_SupplyingGeometryFields_UpdatesStoredGeometry()
    {
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            width = 200.00m,
            height = 150.00m,
            shape = "square",
            rotation = 90,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await LoadTableAsync(tableId);
        stored.Width.Should().Be(200.00m);
        stored.Height.Should().Be(150.00m);
        stored.Shape.Should().Be("square");
        stored.Rotation.Should().Be(90);
    }

    [Theory]
    [InlineData(5, 80, 0, "Width")]        // width below [Range(10, 500)]
    [InlineData(80, 900, 0, "Height")]     // height above [Range(10, 500)]
    [InlineData(80, 80, 400, "Rotation")]  // rotation above [Range(0, 360)]
    public async Task Put_OutOfRangeGeometry_Returns400AndLeavesStoredGeometryUnchanged(
        decimal width, decimal height, int rotation, string expectedField)
    {
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            width,
            height,
            shape = "square",
            rotation,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Naming the offending field proves the 400 came from the DataAnnotation
        // and not from the handler's catch-all, which also surfaces as a 400.
        (await response.Content.ReadAsStringAsync()).Should().Contain(expectedField);

        var stored = await LoadTableAsync(tableId);
        stored.Width.Should().Be(SeededWidth);
        stored.Height.Should().Be(SeededHeight);
        stored.Shape.Should().Be(SeededShape);
        stored.Rotation.Should().Be(SeededRotation);
    }

    [Fact]
    public async Task Put_OverlongShape_Returns400AndLeavesStoredShapeUnchanged()
    {
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            shape = new string('x', 21),  // one over [MaxLength(20)]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shape");

        var stored = await LoadTableAsync(tableId);
        stored.Shape.Should().Be(SeededShape);
    }

    [Fact]
    public async Task Put_BlankShape_LeavesStoredShapeUnchanged()
    {
        // The shape column was created NOT NULL DEFAULT '', so a blank string
        // is a legacy placeholder rather than a shape — it must not overwrite
        // a real stored value.
        var tableId = await SeedTableAsync();
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"{Endpoint}/{tableId}", new
        {
            tableNumber = TableNumber,
            maxGuests = 4,
            isActive = true,
            isOutdoor = false,
            positionX = 10.00m,
            positionY = 20.00m,
            shape = "",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await LoadTableAsync(tableId);
        stored.Shape.Should().Be(SeededShape);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedTableAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var table = new Table
        {
            Id = Guid.NewGuid(),
            TableNumber = TableNumber,
            MaxGuests = 4,
            IsActive = true,
            IsOutdoor = false,
            PositionX = 100.00m,
            PositionY = 100.00m,
            Width = SeededWidth,
            Height = SeededHeight,
            Shape = SeededShape,
            Rotation = SeededRotation,
            CreatedBy = "seed",
            CreatedAt = DateTime.UtcNow,
        };

        db.Tables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task<Table> LoadTableAsync(Guid tableId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var table = await db.Tables.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tableId);
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
