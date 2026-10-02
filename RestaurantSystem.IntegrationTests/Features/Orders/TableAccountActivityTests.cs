using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class TableAccountActivityTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Activity_is_bounded_per_visit_and_keeps_cancelled_rounds_without_private_notes()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.TableServiceSessions.AddRange(Session(first, 81), Session(second, 82));
        var busy = Order(first, OrderStatus.Confirmed);
        var cancelled = Order(second, OrderStatus.Cancelled);
        context.Orders.AddRange(busy, cancelled);
        for (var index = 0; index < TableAccountActivityReader.PageSize + 10; index++)
        {
            context.OrderStatusHistories.Add(History(busy.Id, index));
        }
        var cancellation = History(cancelled.Id, 0);
        cancellation.ToStatus = OrderStatus.Cancelled;
        cancellation.Notes = "Private operator context";
        context.OrderStatusHistories.Add(cancellation);
        await context.SaveChangesAsync();

        var pages = await TableAccountActivityReader.ReadManyAsync(context, [first, second], CancellationToken.None);

        pages[first].Events.Should().HaveCount(100);
        pages[first].HasMore.Should().BeTrue();
        pages[second].Events.Should().HaveCount(2, "one busy visit cannot crowd out another visit's events");
        pages[second].HasMore.Should().BeFalse();
        pages[second].Events.Should().Contain(entry => entry.Id == cancellation.Id
            && entry.OrderId == cancelled.Id && entry.Kind == "StatusChanged"
            && entry.Status == nameof(OrderStatus.Cancelled));
        pages[first].Events.Select(entry => entry.OccurredAt).Should().BeInDescendingOrder();
        typeof(RestaurantSystem.Api.Features.Orders.Dtos.TableAccountActivityDto).GetProperties()
            .Select(property => property.Name).Should().NotContain("Notes").And.NotContain("ChangedBy");
    }

    private static TableServiceSession Session(Guid id, int number) => new()
    {
        Id = id,
        TableNumber = number,
        OpenedAt = DateTime.UtcNow,
        Status = TableServiceSessionStatus.Open,
        CreatedBy = nameof(TableAccountActivityTests)
    };

    private static Order Order(Guid sessionId, OrderStatus status)
    {
        var id = Guid.NewGuid();
        return new Order
        {
            Id = id,
            OrderNumber = $"AC-{id:N}"[..16],
            Type = OrderType.DineIn,
            ServiceSessionId = sessionId,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow.AddHours(-1),
            CreatedBy = nameof(TableAccountActivityTests)
        };
    }

    private static OrderStatusHistory History(Guid orderId, int index) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        FromStatus = OrderStatus.Confirmed,
        ToStatus = OrderStatus.Preparing,
        ChangedAt = DateTime.UtcNow.AddSeconds(index),
        ChangedBy = nameof(TableAccountActivityTests),
        CreatedBy = nameof(TableAccountActivityTests)
    };
}
