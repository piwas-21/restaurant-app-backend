using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>Public table availability must not disclose the staff occupancy projection.</summary>
[Collection("Database Lane 3")]
public sealed class TableOccupancySecurityTests : IntegrationTestBase
{
    private const string OccupiedTableNumber = "9701";
    private const string EmptySessionTableNumber = "9702";

    public TableOccupancySecurityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task PublicTableListKeepsAvailabilityButOmitsLiveOccupancy()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync("/api/tables");
        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await ReadResponseAsync<ApiResponse<List<TableDto>>>(response);
        var table = result!.Data!.Single(value => value.TableNumber == OccupiedTableNumber);

        table.IsActive.Should().BeTrue();
        table.IsOccupied.Should().BeFalse();
        table.ActiveOrderCount.Should().Be(0);
        table.Occupants.Should().BeNull();
    }

    [Fact]
    public async Task StaffTableListIncludesOpenSessionsAndCompletedEligibleDebt()
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync("/api/tables/occupancy");
        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await ReadResponseAsync<ApiResponse<List<TableDto>>>(response);
        var tables = result!.Data!;
        var occupied = tables.Single(value => value.TableNumber == OccupiedTableNumber);
        var emptySession = tables.Single(value => value.TableNumber == EmptySessionTableNumber);

        occupied.IsOccupied.Should().BeTrue();
        occupied.ActiveOrderCount.Should().Be(2, "the projection counts every eligible order");
        occupied.Occupants.Should().ContainSingle(value =>
            value.OrderNumber == "OCC-9701-LATEST");
        emptySession.IsOccupied.Should().BeTrue();
        emptySession.ActiveOrderCount.Should().Be(0);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        context.Tables.AddRange(
            new Table
            {
                Id = Guid.NewGuid(),
                TableNumber = OccupiedTableNumber,
                MaxGuests = 4,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = nameof(TableOccupancySecurityTests),
            },
            new Table
            {
                Id = Guid.NewGuid(),
                TableNumber = EmptySessionTableNumber,
                MaxGuests = 2,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = nameof(TableOccupancySecurityTests),
            });
        context.TableServiceSessions.AddRange(
            NewSession(9701, now),
            NewSession(9702, now));
        context.Orders.AddRange(
            new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "OCC-9701",
                Type = OrderType.DineIn,
                TableNumber = 9701,
                Status = OrderStatus.Completed,
                PaymentStatus = PaymentStatus.Pending,
                SubTotal = 12m,
                Total = 12m,
                TotalPaid = 0m,
                RemainingAmount = 12m,
                OrderDate = now,
                CreatedAt = now,
                CreatedBy = nameof(TableOccupancySecurityTests),
            },
            new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "OCC-9701-LATEST",
                Type = OrderType.DineIn,
                TableNumber = 9701,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                SubTotal = 8m,
                Total = 8m,
                TotalPaid = 0m,
                RemainingAmount = 8m,
                OrderDate = now.AddMinutes(1),
                CreatedAt = now.AddMinutes(1),
                CreatedBy = nameof(TableOccupancySecurityTests),
            });
        await context.SaveChangesAsync();
    }

    private static TableServiceSession NewSession(int tableNumber, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        TableNumber = tableNumber,
        Status = TableServiceSessionStatus.Open,
        Version = 1,
        OpenedAt = now,
        CreatedAt = now,
        CreatedBy = nameof(TableOccupancySecurityTests),
    };
}
