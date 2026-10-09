using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderPaymentTipsMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261009131247_AddTableSessionLifecycleAndTenderTips";
    private const string TipsMigration = "20261009140008_AddOrderPaymentTips";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Nonzero_tip_history_blocks_rollback_without_changing_schema_or_values()
    {
        var paymentId = await SeedPaymentAsync(tipMinor: 325, refundedTipMinor: 125);
        try
        {
            var rollback = () => MigrateAsync(PreviousMigration);
            var refusal = (await rollback.Should().ThrowAsync<PostgresException>()).Which;
            refusal.SqlState.Should().Be("23514");
            refusal.MessageText.Should().Be("Order payment gratuity history must be retained; use a forward migration");

            (await LatestAppliedMigrationAsync()).Should().Be(TipsMigration);
            await using var verify = fixture.CreateContext();
            var payment = await verify.OrderPayments.AsNoTracking().SingleAsync(value => value.Id == paymentId);
            payment.TipMinor.Should().Be(325);
            payment.RefundedTipMinor.Should().Be(125);
        }
        finally
        {
            await MigrateAsync();
            await fixture.ResetDatabaseAsync();
        }
    }

    private async Task<Guid> SeedPaymentAsync(long tipMinor, long refundedTipMinor)
    {
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderNumber = $"TIP-{Guid.NewGuid():N}"[..15],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            OrderDate = now,
            Total = 20m,
            TotalPaid = 20m,
            RemainingAmount = 0m,
            CreatedBy = nameof(OrderPaymentTipsMigrationTests)
        };
        var payment = new OrderPayment
        {
            Order = order,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 20m,
            TipMinor = tipMinor,
            RefundedTipMinor = refundedTipMinor,
            Status = PaymentStatus.PartiallyRefunded,
            PaymentDate = now,
            CreatedBy = nameof(OrderPaymentTipsMigrationTests)
        };

        await using var context = fixture.CreateContext();
        context.Orders.Add(order);
        context.OrderPayments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task<string> LatestAppliedMigrationAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
