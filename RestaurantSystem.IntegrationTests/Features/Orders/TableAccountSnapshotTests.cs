using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class TableAccountSnapshotTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("session")]
    [InlineData("active")]
    [InlineData("bill")]
    public async Task Account_content_and_revision_are_from_the_same_snapshot(string mode)
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 72,
                Version = 1,
                AccountRevision = 1,
                Status = TableServiceSessionStatus.Open,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = nameof(TableAccountSnapshotTests)
            });
            seed.Orders.Add(new Order
            {
                Id = orderId,
                OrderNumber = $"SN-{orderId:N}"[..16],
                Type = OrderType.DineIn,
                ServiceSessionId = sessionId,
                TableNumber = 72,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                SubTotal = 10m,
                Total = 10m,
                RemainingAmount = 10m,
                OrderDate = DateTime.UtcNow,
                CreatedBy = nameof(TableAccountSnapshotTests)
            });
            await seed.SaveChangesAsync();
        }

        var gate = new OrderReadGate();
        await using var context = DatabaseFixture.CreateContext(gate);
        var assembler = new TableBillAssembler(context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance), NullLogger<TableBillAssembler>.Instance);
        var reader = new TableServiceSessionReader(context, assembler);
        var read = ReadBillAsync();
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var writer = DatabaseFixture.CreateContext();
            var session = await writer.TableServiceSessions.SingleAsync(value => value.Id == sessionId);
            session.RecordAccountChange();
            var order = await writer.Orders.SingleAsync(value => value.Id == orderId);
            order.Total = 20m;
            order.SubTotal = 20m;
            order.RemainingAmount = 20m;
            await writer.SaveChangesAsync();
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }

        var bill = await read;
        bill.AccountRevision.Should().Be(1);
        bill.Total.Should().Be(10m, "content must match the revision read before the concurrent change");
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId))
            .AccountRevision.Should().Be(2, "the concurrent write really committed");

        async Task<TableBillDto> ReadBillAsync() => mode switch
        {
            "session" => (await reader.ReadAsync(sessionId, CancellationToken.None))!.Bill,
            "active" => (await reader.ReadActiveAsync(CancellationToken.None))
                .Single(value => value.ServiceSessionId == sessionId).Bill,
            _ => (await assembler.AssembleAsync(sessionId, CancellationToken.None))!
        };
    }

    [Theory]
    [InlineData("SELECT o.id FROM orders AS o", true)]
    [InlineData("SELECT o.id FROM \"orders\" AS o", true)]
    [InlineData("SELECT p.id FROM order_payments AS p", false)]
    [InlineData("SELECT p.id FROM orders_archive AS p", false)]
    [InlineData("SELECT s.id FROM \"TableServiceSessions\" AS s", false)]
    public void Read_gate_distinguishes_order_queries(string sql, bool expected) =>
        OrderReadGate.IsOrderRead(sql).Should().Be(expected);

    private sealed class OrderReadGate : DbCommandInterceptor
    {
        private int _arrivals;
        public TaskCompletionSource<bool> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsOrderRead(command.CommandText)
                && Interlocked.Increment(ref _arrivals) == 1)
            {
                Arrived.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            return result;
        }

        public static bool IsOrderRead(string sql) =>
            sql.Contains("FROM orders AS ", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("FROM \"orders\" AS ", StringComparison.OrdinalIgnoreCase);
    }
}
