using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CompleteAllTableOrdersCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Legacy table-clear safety once explicit table visits exist.</summary>
[Collection("Database Lane 3")]
public sealed class CompleteAllTableOrdersTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public CompleteAllTableOrdersTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Active_explicit_visit_refuses_clear_without_mutating_any_visit()
    {
        var sessionId = await SeedSessionAsync(21, TableServiceSessionStatus.Open);
        var explicitOrder = await SeedOrderAsync(sessionId, 21, OrderStatus.Confirmed);
        var legacyOrder = await SeedOrderAsync(null, 21, OrderStatus.Confirmed);

        var response = await ClearAsync(21);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
        await AssertUnchangedAsync(explicitOrder, legacyOrder);
    }

    [Fact]
    public async Task Ambiguous_explicit_visits_refuse_clear_without_cancelling_across_visits()
    {
        var firstSession = await SeedSessionAsync(22, TableServiceSessionStatus.Closed);
        var secondSession = await SeedSessionAsync(22, TableServiceSessionStatus.Closed);
        var firstOrder = await SeedOrderAsync(firstSession, 22, OrderStatus.Confirmed);
        var secondOrder = await SeedOrderAsync(secondSession, 22, OrderStatus.Preparing);

        var response = await ClearAsync(22);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
        await AssertUnchangedAsync(firstOrder, secondOrder);
    }

    [Fact]
    public async Task Legacy_visit_without_explicit_membership_still_clears()
    {
        var ready = await SeedOrderAsync(null, 23, OrderStatus.Ready);
        var pending = await SeedOrderAsync(null, 23, OrderStatus.Pending);

        var response = await ClearAsync(23);

        response.Success.Should().BeTrue();
        response.Data!.CompletedCount.Should().Be(1);
        response.Data.CancelledCount.Should().Be(1);
        await using var context = _fixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == ready)).Status
            .Should().Be(OrderStatus.Completed);
        (await context.Orders.SingleAsync(order => order.Id == pending)).Status
            .Should().Be(OrderStatus.Cancelled);
    }

    private async Task<ApiResponse<CompleteAllTableOrdersResult>> ClearAsync(int tableNumber)
    {
        await using var context = _fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.GetAuditIdentifier())
            .Returns(nameof(CompleteAllTableOrdersTests));
        var handler = new CompleteAllTableOrdersCommandHandler(
            context,
            currentUser.Object,
            NullLogger<CompleteAllTableOrdersCommandHandler>.Instance);

        return await handler.Handle(
            new CompleteAllTableOrdersCommand(tableNumber.ToString()), CancellationToken.None);
    }

    private async Task AssertUnchangedAsync(params Guid[] orderIds)
    {
        await using var context = _fixture.CreateContext();
        var orders = await context.Orders
            .Where(order => orderIds.Contains(order.Id))
            .ToListAsync();
        orders.Should().HaveSameCount(orderIds);
        orders.Should().OnlyContain(order =>
            order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.Preparing);
        (await context.OrderStatusHistories.CountAsync(history => orderIds.Contains(history.OrderId)))
            .Should().Be(0);
    }

    private async Task<Guid> SeedSessionAsync(int tableNumber, TableServiceSessionStatus status)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = id,
            TableNumber = tableNumber,
            Status = status,
            Version = 1,
            OpenedAt = SeedTime,
            ClosedAt = status == TableServiceSessionStatus.Closed ? SeedTime.AddHours(1) : null,
            CreatedAt = SeedTime,
            CreatedBy = nameof(CompleteAllTableOrdersTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedOrderAsync(
        Guid? serviceSessionId, int tableNumber, OrderStatus status)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = id,
            OrderNumber = $"CA-{id:N}"[..12],
            Type = OrderType.DineIn,
            TableNumber = tableNumber,
            ServiceSessionId = serviceSessionId,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = SeedTime,
            CreatedAt = SeedTime,
            CreatedBy = nameof(CompleteAllTableOrdersTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static readonly DateTime SeedTime =
        new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
}
