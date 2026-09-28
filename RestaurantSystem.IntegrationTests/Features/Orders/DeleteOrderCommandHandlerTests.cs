using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public class DeleteOrderCommandHandlerTests : IntegrationTestBase
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid TableId = Guid.NewGuid();
    private static readonly Guid ReservationId = Guid.NewGuid();

    public DeleteOrderCommandHandlerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        context.Tables.Add(new Table
        {
            Id = TableId,
            TableNumber = "T-DEL",
            MaxGuests = 4,
            CreatedBy = nameof(DeleteOrderCommandHandlerTests),
        });
        context.Orders.Add(new Order
        {
            Id = OrderId,
            OrderNumber = $"DEL-{OrderId:N}"[..16],
            Type = OrderType.DineIn,
            TableNumber = 42,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 12m,
            Total = 12m,
            TotalPaid = 0m,
            RemainingAmount = 12m,
            OrderDate = now,
            CreatedBy = nameof(DeleteOrderCommandHandlerTests),
        });
        context.TableReservations.Add(new TableReservation
        {
            Id = ReservationId,
            TableId = TableId,
            TableNumber = "T-DEL",
            OrderId = OrderId,
            ReservedAt = now,
            ReservedUntil = now.AddHours(1),
            IsActive = true,
            CreatedBy = nameof(DeleteOrderCommandHandlerTests),
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_WithCurrentVersion_SoftDeletesAndRetainsJournalAndReservationAudit()
    {
        var journalBefore = await ReadOrderChangesAsync();
        journalBefore.Should().ContainSingle(change => change.Kind == OrderChangeKind.Upsert);

        AuthenticateAsAdmin();
        using var response = await Client.DeleteAsync($"/api/orders/{OrderId}?expectedVersion=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await ReadResponseAsync<ApiResponse<bool>>(response))!;
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Order deleted successfully");
        result.Data.Should().BeTrue();

        using var getResponse = await Client.GetAsync($"/api/orders/{OrderId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getResult = (await ReadResponseAsync<ApiResponse<OrderDto>>(getResponse))!;
        getResult.Success.Should().BeFalse();
        getResult.Errors.Should().Contain("Order not found");

        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.IgnoreQueryFilters().SingleAsync(item => item.Id == OrderId);
        order.IsDeleted.Should().BeTrue();
        order.Version.Should().Be(2);
        order.DeletedAt.Should().NotBeNull();
        order.DeletedBy.Should().Be(TestAuthHandler.AdminUserId);

        var journalAfter = await context.OrderChanges.AsNoTracking()
            .Where(change => change.OrderId == OrderId)
            .OrderBy(change => change.Sequence)
            .ToListAsync();
        journalAfter.Should().HaveCount(journalBefore.Count + 1);
        journalAfter.Take(journalBefore.Count).Select(change => change.Sequence)
            .Should().Equal(journalBefore.Select(change => change.Sequence));
        journalAfter.Should().OnlyContain(change => change.Kind == OrderChangeKind.Upsert);
        order.LastChangeSequence.Should().Be(journalAfter[^1].Sequence);

        var reservation = await context.TableReservations.SingleAsync(item => item.Id == ReservationId);
        reservation.IsActive.Should().BeFalse();
        reservation.ReleasedAt.Should().NotBeNull();
        reservation.ReleasedBy.Should().Be(TestAuthHandler.AdminUserId);
        reservation.ReleaseReason.Should().Be("OrderDeleted");
    }

    [Fact]
    public async Task Delete_WithStaleVersion_LeavesOrderJournalAndReservationUnchanged()
    {
        await using (var updateContext = DatabaseFixture.CreateContext())
        {
            var order = await updateContext.Orders.SingleAsync(item => item.Id == OrderId);
            order.Notes = "Updated before stale delete";
            await updateContext.SaveChangesAsync();
        }

        var journalBefore = await ReadOrderChangesAsync();
        journalBefore.Should().HaveCount(2);
        var lastSequenceBefore = journalBefore[^1].Sequence;

        AuthenticateAsAdmin();
        using var response = await Client.DeleteAsync($"/api/orders/{OrderId}?expectedVersion=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await ReadResponseAsync<ApiResponse<bool>>(response))!;
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);

        await using var context = DatabaseFixture.CreateContext();
        var orderAfter = await context.Orders.SingleAsync(item => item.Id == OrderId);
        orderAfter.Version.Should().Be(2);
        orderAfter.IsDeleted.Should().BeFalse();
        orderAfter.Notes.Should().Be("Updated before stale delete");

        var journalAfter = await context.OrderChanges.AsNoTracking()
            .Where(change => change.OrderId == OrderId)
            .OrderBy(change => change.Sequence)
            .ToListAsync();
        journalAfter.Select(change => change.Sequence).Should()
            .Equal(journalBefore.Select(change => change.Sequence));
        orderAfter.LastChangeSequence.Should().Be(lastSequenceBefore);

        var reservation = await context.TableReservations.SingleAsync(item => item.Id == ReservationId);
        reservation.IsActive.Should().BeTrue();
        reservation.ReleasedAt.Should().BeNull();
        reservation.ReleasedBy.Should().BeNull();
        reservation.ReleaseReason.Should().BeNull();
    }

    private async Task<List<OrderChange>> ReadOrderChangesAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.OrderChanges.AsNoTracking()
            .Where(change => change.OrderId == OrderId)
            .OrderBy(change => change.Sequence)
            .ToListAsync();
    }

}
