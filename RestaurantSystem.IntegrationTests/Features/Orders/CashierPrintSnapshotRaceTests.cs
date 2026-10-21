using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CashierPrintSnapshotRaceTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Concurrent_sibling_void_cannot_mix_new_money_with_old_lines_in_durable_bill()
    {
        var root = Order("SNAPSHOT-ROOT", 10);
        var extra = Order("SNAPSHOT-EXTRA", 8);
        extra.AmendmentRootOrderId = root.Id;
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.AddRange(root, extra);
            await seed.SaveChangesAsync();
        }
        var gate = new AmendmentReadGate();
        await using var context = DatabaseFixture.CreateContext(gate);
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var reader = new CurrentStaffTicketReader(context, services.GetRequiredService<IOrderQueueProjection>(),
            services.GetRequiredService<IOptions<CurrentStaffTicketSettings>>());
        var queue = new CashierPrintQueue(context, services.GetRequiredService<ITableBillAssembler>(),
            services.GetRequiredService<IOrderMappingService>(), reader,
            services.GetRequiredService<ICurrentUserService>(), services.GetRequiredService<IOptions<CashierPrintSettings>>());
        var printing = queue.QueueAsync(new QueueCashierPrintRequest(Guid.NewGuid(), root.Id, null, root.Version),
            false, CancellationToken.None);
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var writer = DatabaseFixture.CreateContext();
            var sibling = await writer.Orders.SingleAsync(order => order.Id == extra.Id);
            sibling.BillingCreditAmount = 8;
            sibling.RemainingAmount = 0;
            var item = extra.Items.Single();
            var before = new OrderItemDto { Id = item.Id, ProductName = item.ProductName, Quantity = 1, ItemTotal = 8 };
            writer.OrderAmendments.Add(new OrderAmendment
            {
                SourceOrderId = sibling.Id,
                State = OrderAmendmentState.Committed,
                ActorUserId = Guid.NewGuid(),
                ActorRole = "Cashier",
                PayloadHash = new string('A', 64),
                RequestJson = "{}",
                SourceSnapshotJson = "{}",
                FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
                "CHF", 0, 800, -800, 800, OrderAmendmentFinancialResolutionStatus.NotRequired,
                OrderAmendmentCreditState.None, OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None)),
                ChangesJson = OrderAmendmentJson.Serialize(new OrderAmendmentChangeSnapshot[]
                { new(item.Id, OrderAmendmentChangeKind.Void, 1, 1, false, before, null) }),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedBy = nameof(CashierPrintSnapshotRaceTests)
            });
            await writer.SaveChangesAsync();
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }
        await printing;
        var saved = await context.CashierPrintJobs.AsNoTracking().SingleAsync();
        var bill = JsonSerializer.Deserialize<OrderDto>(saved.SnapshotJson, JsonOptions)!;
        bill.Items.Should().HaveCount(2);
        bill.SubTotal.Should().Be(18);
        bill.PayableTotal.Should().Be(18, "the receipt must retain the complete state before the concurrent commit");
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.SingleAsync(order => order.Id == extra.Id)).BillingCreditAmount.Should().Be(8);
    }

    private static Order Order(string number, decimal total)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(CashierPrintSnapshotRaceTests)
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = number,
            Quantity = 1,
            UnitPrice = total,
            ItemTotal = total,
            CreatedBy = nameof(CashierPrintSnapshotRaceTests)
        });
        return order;
    }

    private sealed class AmendmentReadGate : DbCommandInterceptor
    {
        private int _arrivals;
        public TaskCompletionSource<bool> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM order_amendments AS ", StringComparison.Ordinal)
                && Interlocked.Increment(ref _arrivals) == 1)
            {
                Arrived.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
