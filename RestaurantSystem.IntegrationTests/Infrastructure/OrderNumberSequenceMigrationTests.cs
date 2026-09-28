using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderNumberSequenceMigrationTests
{
    private const string MigrationBeforeSequence = "20260927174440" + "_AddOptionSetMaterializationJobs";
    private static readonly DateTimeOffset AllocationInstant = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTenantClock AllocationClock = new("UTC", AllocationInstant);

    private readonly DatabaseFixture _fixture;

    public OrderNumberSequenceMigrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_backfills_deleted_numbers_and_trigger_tracks_legacy_inserts()
    {
        var day = DateOnly.FromDateTime(AllocationInstant.UtcDateTime);
        var prefix = day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var activeOrderId = Guid.NewGuid();
        var deletedOrderId = Guid.NewGuid();
        var legacyInsertId = Guid.NewGuid();
        var invalidDateId = Guid.NewGuid();
        var legacyTextId = Guid.NewGuid();
        var allocatedOrderId = Guid.NewGuid();

        try
        {
            await using (var setup = _fixture.CreateContext())
            {
                await setup.Database.MigrateAsync(MigrationBeforeSequence);
                setup.Orders.Add(CreateOrder(activeOrderId, $"{prefix}0003", isDeleted: false));
                setup.Orders.Add(CreateOrder(deletedOrderId, $"{prefix}0008", isDeleted: true));
                await setup.SaveChangesAsync();
            }

            await using (var migrate = _fixture.CreateContext())
            {
                await migrate.Database.MigrateAsync();

                var backfilled = await migrate.OrderNumberSequences.SingleAsync(row => row.Day == day);
                backfilled.LastSequence.Should().Be(8,
                    "the backfill must include canonical order numbers on soft-deleted rows");

                // This insert models an older app instance that still allocates from orders.
                // The database trigger keeps the durable watermark in sync after backfill.
                migrate.Orders.Add(CreateOrder(legacyInsertId, $"{prefix}0009", isDeleted: false));
                migrate.Orders.Add(CreateOrder(invalidDateId, "202613990001", isDeleted: false));
                migrate.Orders.Add(CreateOrder(legacyTextId, $"LEGACY-{legacyTextId:N}"[..16], isDeleted: false));
                await migrate.SaveChangesAsync();

                (await migrate.OrderNumberSequences.AsNoTracking().SingleAsync(row => row.Day == day))
                    .LastSequence.Should().Be(9,
                        "valid legacy inserts advance the watermark while noncanonical values are ignored");

                const string invalidDate = "202613990001";
                var parsedInvalidDate = await migrate.Database
                    .SqlQuery<DateOnly?>($"SELECT try_order_number_day({invalidDate}) AS \"Value\"")
                    .SingleAsync();
                parsedInvalidDate.Should().BeNull();

                const string overflowNumber = "20000101" + "9999999999999999999";
                var parsedOverflow = await migrate.Database
                    .SqlQuery<long?>($"SELECT try_order_number_sequence({overflowNumber}) AS \"Value\"")
                    .SingleAsync();
                parsedOverflow.Should().BeNull();

                await using var transaction = await migrate.Database.BeginTransactionAsync();
                var nextNumber = await new OrderNumberGenerator(migrate, AllocationClock)
                    .GenerateAsync();
                nextNumber.Should().Be($"{prefix}0010");
                migrate.Orders.Add(CreateOrder(allocatedOrderId, nextNumber, isDeleted: false));
                await migrate.SaveChangesAsync();
                await transaction.CommitAsync();

                (await migrate.OrderNumberSequences.AsNoTracking().SingleAsync(row => row.Day == day))
                    .LastSequence.Should().Be(10);
            }
        }
        finally
        {
            await using var cleanup = _fixture.CreateContext();
            await cleanup.Database.MigrateAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {activeOrderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {deletedOrderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {legacyInsertId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {invalidDateId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {legacyTextId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {allocatedOrderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_number_sequences WHERE day = {day}");

            await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE orders DISABLE TRIGGER orders_queue_sequence;");
            try
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {activeOrderId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {deletedOrderId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {legacyInsertId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {invalidDateId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {legacyTextId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {allocatedOrderId}");
            }
            finally
            {
                await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE orders ENABLE TRIGGER orders_queue_sequence;");
            }
        }
    }

    private static Order CreateOrder(Guid id, string orderNumber, bool isDeleted) => new()
    {
        Id = id,
        OrderNumber = orderNumber,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 0m,
        Total = 0m,
        TotalPaid = 0m,
        RemainingAmount = 0m,
        OrderDate = AllocationInstant.UtcDateTime,
        CreatedAt = AllocationInstant.UtcDateTime,
        CreatedBy = nameof(OrderNumberSequenceMigrationTests),
        IsDeleted = isDeleted,
        DeletedAt = isDeleted ? AllocationInstant.UtcDateTime : null,
        DeletedBy = isDeleted ? nameof(OrderNumberSequenceMigrationTests) : null,
    };
}
