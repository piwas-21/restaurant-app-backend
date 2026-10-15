using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderNumberSequenceMigrationTests : IAsyncLifetime
{
    private const string MigrationBeforeSequence = "20260927174440" + "_AddOptionSetMaterializationJobs";
    private static readonly DateTimeOffset AllocationInstant = new(2000, 1, 1, 3, 30, 0, TimeSpan.Zero);
    private static readonly FixedTenantClock AllocationClock = new("America/Los_Angeles", AllocationInstant);

    private readonly DatabaseFixture _fixture;

    public OrderNumberSequenceMigrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // Historical-schema probes need an empty disposable lane; production Down still
    // refuses any financial history. No trigger or migration guard is bypassed.
    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_backfills_deleted_numbers_and_trigger_tracks_legacy_inserts()
    {
        var day = DateOnly.FromDateTime(AllocationClock.ToTenantTime(AllocationInstant.UtcDateTime).DateTime);
        var prefix = day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var activeOrderId = Guid.NewGuid();
        var deletedOrderId = Guid.NewGuid();
        var validTenantDayOrderId = Guid.NewGuid();
        var malformedTenantDayOrderId = Guid.NewGuid();
        var legacyInsertId = Guid.NewGuid();
        var invalidDateId = Guid.NewGuid();
        var legacyTextId = Guid.NewGuid();
        var allocatedOrderId = Guid.NewGuid();

        try
        {
            await using (var setup = _fixture.CreateContext())
            {
                await setup.Database.MigrateAsync(MigrationBeforeSequence);
                await HistoricalOrderSeed.InsertAsync(setup, activeOrderId, $"{prefix}0003",
                    AllocationInstant.UtcDateTime, nameof(OrderNumberSequenceMigrationTests));
                await HistoricalOrderSeed.InsertAsync(setup, deletedOrderId, $"{prefix}0008",
                    AllocationInstant.UtcDateTime, nameof(OrderNumberSequenceMigrationTests), deleted: true);
                await HistoricalOrderSeed.InsertAsync(setup, validTenantDayOrderId, $"{prefix}0011",
                    AllocationInstant.UtcDateTime.AddHours(4), nameof(OrderNumberSequenceMigrationTests));
                await HistoricalOrderSeed.InsertAsync(setup, malformedTenantDayOrderId,
                    $"BCR-{malformedTenantDayOrderId:N}"[..20], AllocationInstant.UtcDateTime,
                    nameof(OrderNumberSequenceMigrationTests));
            }

            await using (var migrate = _fixture.CreateContext())
            {
                await migrate.Database.MigrateAsync();
                await new OrderDailyIdentityInitializer(
                    migrate, AllocationClock, NullLogger<OrderDailyIdentityInitializer>.Instance)
                    .InitializeAsync();

                var malformedIdentity = await migrate.Orders.AsNoTracking()
                    .SingleAsync(order => order.Id == malformedTenantDayOrderId);
                malformedIdentity.BusinessDate.Should().Be(day,
                    "malformed legacy references must fall back to the tenant-local order date");
                malformedIdentity.DailyOrderNumber.Should().BeGreaterThan(11,
                    "fallback allocation must stay beyond every valid parsed daily identity");

                var dayIdentities = await migrate.Orders.IgnoreQueryFilters().AsNoTracking()
                    .Where(order => order.BusinessDate == day)
                    .Select(order => order.DailyOrderNumber)
                    .ToListAsync();
                dayIdentities.Should().OnlyHaveUniqueItems();
                dayIdentities.Should().Contain(3).And.Contain(8).And.Contain(11);
                var backfilled = await migrate.OrderNumberSequences.SingleAsync(row => row.Day == day);
                backfilled.LastSequence.Should().Be(dayIdentities.Max(),
                    "the startup backfill must include soft-deleted, valid, and fallback identities in its watermark");

                // This raw SQL insert models an older app instance that does not know the new
                // NOT NULL columns. The trigger must derive their values and maintain the
                // sequence watermark before the next version allocates.
                var legacySequence = backfilled.LastSequence + 1;
                await HistoricalOrderSeed.InsertAsync(migrate, legacyInsertId, $"{prefix}{legacySequence:D4}",
                    AllocationInstant.UtcDateTime, nameof(OrderNumberSequenceMigrationTests));
                var legacyIdentity = await migrate.Orders.AsNoTracking()
                    .SingleAsync(order => order.Id == legacyInsertId);
                legacyIdentity.BusinessDate.Should().Be(day);
                legacyIdentity.DailyOrderNumber.Should().Be(legacySequence);

                // Invalid and noncanonical values remain accepted as historical text, but their
                // fallback tenant-local days are isolated from the canonical sequence above.
                migrate.Orders.Add(CreateOrder(invalidDateId, "202613990001", isDeleted: false,
                    AllocationInstant.UtcDateTime.AddDays(1)));
                migrate.Orders.Add(CreateOrder(legacyTextId, $"LEGACY-{legacyTextId:N}"[..16], isDeleted: false,
                    AllocationInstant.UtcDateTime.AddDays(2)));
                await migrate.SaveChangesAsync();

                (await migrate.OrderNumberSequences.AsNoTracking().SingleAsync(row => row.Day == day))
                    .LastSequence.Should().Be(legacySequence,
                        "valid legacy inserts advance the watermark while noncanonical references stay on their assigned local days");

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
                var nextSequence = legacySequence + 1;
                nextNumber.OrderNumber.Should().Be($"{prefix}{nextSequence:D4}");
                migrate.Orders.Add(CreateOrder(allocatedOrderId, nextNumber.OrderNumber, isDeleted: false));
                await migrate.SaveChangesAsync();
                await transaction.CommitAsync();

                (await migrate.OrderNumberSequences.AsNoTracking().SingleAsync(row => row.Day == day))
                    .LastSequence.Should().Be(nextSequence);
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
                $"DELETE FROM order_changes WHERE order_id = {validTenantDayOrderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {malformedTenantDayOrderId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {legacyInsertId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {invalidDateId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {legacyTextId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_changes WHERE order_id = {allocatedOrderId}");
            var sequenceDays = new[]
            {
                day,
                DateOnly.FromDateTime(AllocationClock.ToTenantTime(
                    AllocationInstant.UtcDateTime.AddDays(1)).DateTime),
                DateOnly.FromDateTime(AllocationClock.ToTenantTime(
                    AllocationInstant.UtcDateTime.AddDays(2)).DateTime)
            };
            foreach (var sequenceDay in sequenceDays)
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM order_number_sequences WHERE day = {sequenceDay}");

            await cleanup.Database.ExecuteSqlRawAsync("ALTER TABLE orders DISABLE TRIGGER orders_queue_sequence;");
            try
            {
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {activeOrderId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {deletedOrderId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {validTenantDayOrderId}");
                await cleanup.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM orders WHERE id = {malformedTenantDayOrderId}");
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

    private static Order CreateOrder(Guid id, string orderNumber, bool isDeleted, DateTime? orderDate = null)
    {
        var orderInstant = orderDate ?? AllocationInstant.UtcDateTime;
        return new Order
        {
            Id = id,
            OrderNumber = orderNumber,
            BusinessDate = DateOnly.FromDateTime(AllocationClock.ToTenantTime(orderInstant).DateTime),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 0m,
            Total = 0m,
            TotalPaid = 0m,
            RemainingAmount = 0m,
            OrderDate = orderInstant,
            CreatedAt = orderInstant,
            CreatedBy = nameof(OrderNumberSequenceMigrationTests),
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? orderInstant : null,
            DeletedBy = isDeleted ? nameof(OrderNumberSequenceMigrationTests) : null,
        };
    }
}
