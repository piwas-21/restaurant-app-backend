using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Database proofs for the order detail version and aggregate concurrency contract.</summary>
[Collection("Database Lane 3")]
public sealed class OrderVersionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public OrderVersionTests(DatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await using var context = _fixture.CreateContext();
        await TestDataSeeder.SeedBasicDataAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Scalar_order_edit_increments_the_detail_version()
    {
        var orderId = await SeedOrderAsync();

        await using (var context = _fixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(item => item.Id == orderId);
            order.Notes = "staff context";
            await context.SaveChangesAsync();
            order.Version.Should().Be(2);
        }

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.Where(item => item.Id == orderId).Select(item => item.Version).SingleAsync())
            .Should().Be(2);
    }

    [Fact]
    public async Task Direct_payment_child_edit_touches_the_order_owner()
    {
        var orderId = await SeedOrderAsync();

        await using (var context = _fixture.CreateContext())
        {
            var order = await context.Orders.Include(item => item.Payments)
                .SingleAsync(item => item.Id == orderId);
            order.Payments.Add(new OrderPayment
            {
                OrderId = orderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 5m,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow,
                CreatedBy = nameof(OrderVersionTests),
            });
            await context.SaveChangesAsync();
        }

        await AssertVersionAsync(orderId, 2);
    }

    [Fact]
    public async Task Direct_note_and_status_history_children_touch_the_order_owner()
    {
        var orderId = await SeedOrderAsync();

        await using (var context = _fixture.CreateContext())
        {
            var order = await context.Orders.Include(item => item.StatusHistory)
                .SingleAsync(item => item.Id == orderId);
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                FromStatus = OrderStatus.Pending,
                ToStatus = OrderStatus.Confirmed,
                ChangedAt = DateTime.UtcNow,
                ChangedBy = nameof(OrderVersionTests),
                CreatedBy = nameof(OrderVersionTests),
            });
            context.OrderOperationalNotes.Add(new OrderOperationalNote
            {
                OrderId = orderId,
                Text = "watch the table",
                Audience = OrderNoteAudience.Staff,
                ClientOperationId = Guid.NewGuid(),
                CreatedBy = nameof(OrderVersionTests),
            });
            await context.SaveChangesAsync();
        }

        await AssertVersionAsync(orderId, 2);
    }

    [Fact]
    public async Task Direct_focus_edit_touches_the_order_owner()
    {
        var orderId = await SeedOrderAsync(withFocus: true);

        await using (var context = _fixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(item => item.Id == orderId);
            order.Focus!.Reason = "updated reason";
            await context.SaveChangesAsync();
        }

        await AssertVersionAsync(orderId, 2);
    }

    [Fact]
    public async Task Ingredient_snapshot_edit_touches_its_order_through_the_line_owner()
    {
        var orderId = await SeedOrderAsync();
        Guid itemId;

        await using (var context = _fixture.CreateContext())
        {
            var item = new OrderItem
            {
                OrderId = orderId,
                ProductName = "Snapshot line",
                Quantity = 1,
                UnitPrice = 10m,
                ItemTotal = 10m,
                CreatedBy = nameof(OrderVersionTests),
            };
            context.OrderItems.Add(item);
            await context.SaveChangesAsync();
            itemId = item.Id;
        }

        await using (var context = _fixture.CreateContext())
        {
            context.OrderItemIngredients.Add(new OrderItemIngredient
            {
                OrderItemId = itemId,
                IngredientId = Guid.NewGuid(),
                IngredientName = "Fresh basil",
                Quantity = 1,
                SortOrder = 0,
                CreatedBy = nameof(OrderVersionTests),
            });
            await context.SaveChangesAsync();
        }

        await AssertVersionAsync(orderId, 3);
    }

    [Fact]
    public async Task Checkout_session_mutations_touch_the_order_owner()
    {
        var orderId = await SeedOrderAsync();

        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(item => item.Id == orderId);
        var session = new OrderCheckoutSession
        {
            OrderId = orderId,
            SessionId = $"cs_{Guid.NewGuid():N}",
            Currency = "chf",
            AmountMinor = 1000,
            IdempotencyKey = $"checkout:{orderId}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ConnectedAccountId = "acct_test",
            CreatedBy = nameof(OrderVersionTests),
        };
        context.OrderCheckoutSessions.Add(session);
        await context.SaveChangesAsync();
        order.Version.Should().Be(2);

        session.Status = CheckoutSessionStatus.Expired;
        await context.SaveChangesAsync();
        order.Version.Should().Be(3);
    }

    [Fact]
    public async Task Linked_fidelity_transaction_touches_the_order_owner()
    {
        var orderId = await SeedOrderAsync();

        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(item => item.Id == orderId);
        context.FidelityPointsTransactions.Add(new FidelityPointsTransaction
        {
            UserId = Guid.Parse(TestAuthHandler.UserId),
            OrderId = orderId,
            TransactionType = TransactionType.Earned,
            Points = 100,
            OrderTotal = order.Total,
            CreatedBy = nameof(OrderVersionTests),
        });
        await context.SaveChangesAsync();

        order.Version.Should().Be(2);
        await AssertVersionAsync(orderId, 2);
    }

    [Fact]
    public async Task Concurrent_order_edits_fail_with_the_original_version_token()
    {
        var orderId = await SeedOrderAsync();
        await using var first = _fixture.CreateContext();
        await using var second = _fixture.CreateContext();
        var firstOrder = await first.Orders.SingleAsync(item => item.Id == orderId);
        var secondOrder = await second.Orders.SingleAsync(item => item.Id == orderId);

        firstOrder.Notes = "first writer";
        await first.SaveChangesAsync();
        secondOrder.Notes = "stale writer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await AssertVersionAsync(orderId, 2);
    }

    private async Task<Guid> SeedOrderAsync(bool withFocus = false)
    {
        var orderId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"OV-{orderId:N}"[..12],
            UserId = Guid.Parse(TestAuthHandler.UserId),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow,
            Focus = withFocus
                ? new OrderFocus { Reason = "initial reason", FocusedAt = DateTime.UtcNow }
                : null,
            CreatedBy = nameof(OrderVersionTests),
        });
        await context.SaveChangesAsync();
        return orderId;
    }

    private async Task AssertVersionAsync(Guid orderId, int expected)
    {
        await using var context = _fixture.CreateContext();
        var version = await context.Orders
            .Where(item => item.Id == orderId)
            .Select(item => item.Version)
            .SingleAsync();
        version.Should().Be(expected);
    }
}
