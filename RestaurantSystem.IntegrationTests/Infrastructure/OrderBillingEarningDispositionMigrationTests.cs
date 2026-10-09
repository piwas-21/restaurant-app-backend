using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderBillingEarningDispositionMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PublishedParent = "20261004220409_WithdrawRetainedPrinterInstructions";
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upgrade_rollback_and_reapply_preserve_legacy_unknown_zero_and_awarded_evidence()
    {
        try
        {
            await MigrateAsync(PublishedParent);
            var ids = await SeedPublishedParentEvidenceAsync();
            var before = await CaptureEvidenceAsync();

            await MigrateAsync();
            (await CaptureEvidenceAsync()).Should().Be(before,
                "the additive disposition migration must preserve every pre-existing source, snapshot, unit, owner and award value");
            await AssertEffectiveDispositionsAsync(ids);

            await MigrateAsync(PublishedParent);
            (await CaptureEvidenceAsync()).Should().Be(before,
                "rolling back nullable disposition metadata must leave historical financial evidence intact");
            await MigrateAsync();
            (await CaptureEvidenceAsync()).Should().Be(before,
                "reapplying the additive migration must not rewrite legacy financial evidence");
            await AssertEffectiveDispositionsAsync(ids);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    private async Task<LegacySnapshotIds> SeedPublishedParentEvidenceAsync()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var positive = NewOrder(Guid.NewGuid(), userId, 20m, 2, now);
        positive.FidelityPointsEarned = 80;
        var evaluatedZero = NewOrder(Guid.NewGuid(), userId, 10m, 1, now);
        var unknown = NewOrder(Guid.NewGuid(), null, 10m, 1, now);
        var positiveTransactionId = Guid.NewGuid();
        var positiveBuilt = OrderBillingSnapshotFactory.Build(positive, "CHF",
            new OrderBillingEarningEvaluation(80, "test-evaluator-v1", new string('a', 64),
                new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Frozen rule", 0m, null, 80, 1),
                OrderBillingEarningDisposition.Evaluated), null, 1_000);
        var zeroBuilt = OrderBillingSnapshotFactory.Build(evaluatedZero, "CHF",
            new OrderBillingEarningEvaluation(0, "test-evaluator-v1", new string('b', 64), null,
                OrderBillingEarningDisposition.Evaluated), null, 1_000);
        var unknownBuilt = OrderBillingSnapshotFactory.Build(unknown, "CHF", null, null, 1_000);

        await using (var context = fixture.CreateContext())
        {
            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"migration-{userId:N}",
                NormalizedUserName = $"MIGRATION-{userId:N}",
                FirstName = "Migration",
                LastName = "Fixture",
                Role = UserRole.Customer,
                RefreshToken = string.Empty,
                CreatedAt = now,
                CreatedBy = nameof(OrderBillingEarningDispositionMigrationTests)
            });
            var orders = new[] { positive, evaluatedZero, unknown };
            var items = orders.SelectMany(order => order.Items).ToArray();
            foreach (var order in orders) order.Items.Clear();
            context.Orders.AddRange(orders);
            await context.SaveChangesAsync();
            foreach (var item in items)
                await LegacyEntityFixture.InsertAsync(context, item, "section_id");

            await using var evidenceTransaction = await context.Database.BeginTransactionAsync();
            await LegacyOrderBillingSnapshotFixture.InsertAsync(context, positiveBuilt.Header);
            await LegacyOrderBillingSnapshotFixture.InsertAsync(context, zeroBuilt.Header);
            await LegacyOrderBillingSnapshotFixture.InsertAsync(context, unknownBuilt.Header);
            context.OrderBillingSnapshotUnits.AddRange(positiveBuilt.Units);
            context.OrderBillingSnapshotUnits.AddRange(zeroBuilt.Units);
            context.OrderBillingSnapshotUnits.AddRange(unknownBuilt.Units);
            context.OrderBillingSnapshotOwnerLinks.AddRange(positiveBuilt.OwnerLinks);
            context.OrderBillingSnapshotOwnerLinks.AddRange(zeroBuilt.OwnerLinks);

            context.FidelityPointsTransactions.Add(new FidelityPointsTransaction
            {
                Id = positiveTransactionId,
                UserId = userId,
                OrderId = positive.Id,
                TransactionType = TransactionType.Earned,
                Points = 80,
                OrderTotal = 20m,
                CreatedAt = now,
                CreatedBy = nameof(OrderBillingEarningDispositionMigrationTests)
            });
            var positiveWitness = NewWitness(positiveBuilt.Header, positiveBuilt.OwnerLinks.Single(),
                OrderBillingAwardOutcome.Awarded, 80, 80, positiveTransactionId, now);
            var zeroWitness = NewWitness(zeroBuilt.Header, zeroBuilt.OwnerLinks.Single(),
                OrderBillingAwardOutcome.EvaluatedZero, 0, 0, null, now);
            context.OrderBillingAwardWitnesses.AddRange(positiveWitness, zeroWitness);
            context.OrderBillingAwardUnitCoverages.AddRange(positiveBuilt.Units.Select(unit =>
                new OrderBillingAwardUnitCoverage
                {
                    Id = Guid.NewGuid(),
                    OrderId = positive.Id,
                    SnapshotUnitId = unit.Id,
                    AwardWitnessId = positiveWitness.Id,
                    EligibleEarnedPoints = unit.EarnedPoints,
                    CreatedAt = now,
                    CreatedBy = "OrderBillingAwardBoundary"
                }));
            await context.SaveChangesAsync();
            await evidenceTransaction.CommitAsync();
        }

        return new(positive.Id, evaluatedZero.Id, unknown.Id);
    }

    private static Order NewOrder(Guid orderId, Guid? userId, decimal total, int quantity, DateTime now)
    {
        var itemId = Guid.NewGuid();
        var unitPrice = total / quantity;
        var item = new OrderItem
        {
            Id = itemId,
            OrderId = orderId,
            ProductName = "Frozen migration item",
            Quantity = quantity,
            UnitPrice = unitPrice,
            ItemTotal = total,
            CreatedAt = now,
            CreatedBy = nameof(OrderBillingEarningDispositionMigrationTests)
        };
        return new Order
        {
            Id = orderId,
            OrderNumber = $"MD-{orderId:N}"[..15],
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderBillingEarningDispositionMigrationTests),
            Items = [item]
        };
    }

    private static OrderBillingAwardWitness NewWitness(
        OrderBillingSnapshot snapshot, OrderBillingSnapshotOwnerLink owner,
        OrderBillingAwardOutcome outcome, int candidate, int applied,
        Guid? transactionId, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = snapshot.OrderId,
            OwnerLinkId = owner.Id,
            Outcome = outcome,
            CandidatePoints = candidate,
            AppliedPoints = applied,
            SuppressedPoints = candidate - applied,
            EarnedTransactionId = transactionId,
            CreatedAt = createdAt,
            CreatedBy = "OrderBillingAwardBoundary"
        };

    private async Task AssertEffectiveDispositionsAsync(LegacySnapshotIds ids)
    {
        await using var context = fixture.CreateContext();
        var snapshots = await context.OrderBillingSnapshots.AsNoTracking().ToDictionaryAsync(value => value.OrderId);
        snapshots[ids.Positive].EarnedPointsCandidate.Should().Be(80);
        snapshots[ids.Positive].EarningDisposition.Should().BeNull();
        snapshots[ids.Positive].EffectiveEarningDisposition.Should().Be(OrderBillingEarningDisposition.Evaluated);
        snapshots[ids.Zero].EarnedPointsCandidate.Should().Be(0);
        snapshots[ids.Zero].EarningDisposition.Should().BeNull();
        snapshots[ids.Zero].EffectiveEarningDisposition.Should().Be(OrderBillingEarningDisposition.Evaluated);
        snapshots[ids.Unknown].EarnedPointsCandidate.Should().BeNull();
        snapshots[ids.Unknown].EarningDisposition.Should().BeNull();
        snapshots[ids.Unknown].EffectiveEarningDisposition.Should().Be(OrderBillingEarningDisposition.Unevaluated);
        (await context.OrderBillingAwardWitnesses.CountAsync()).Should().Be(2);
        (await context.OrderBillingAwardUnitCoverages.CountAsync()).Should().Be(2);
        (await context.FidelityPointsTransactions.CountAsync(value => value.TransactionType == TransactionType.Earned))
            .Should().Be(1);
    }

    private async Task<LegacyFinancialEvidence> CaptureEvidenceAsync() => new(
        await CaptureRowsAsync("orders"),
        await CaptureRowsAsync("\"OrderItems\""),
        await CaptureRowsAsync("order_billing_snapshots", removeDisposition: true),
        await CaptureRowsAsync("order_billing_snapshot_units"),
        await CaptureRowsAsync("order_billing_snapshot_owner_links"),
        await CaptureRowsAsync("order_billing_award_witnesses"),
        await CaptureRowsAsync("order_billing_award_unit_coverages"),
        await CaptureRowsAsync("fidelity_points_transactions"));

    private async Task<string> CaptureRowsAsync(string tableName, bool removeDisposition = false)
    {
        var payload = removeDisposition ? "to_jsonb(row) - 'earning_disposition'" : "to_jsonb(row)";
        if (tableName == "\"OrderItems\"") payload += " - 'section_id'";
        var sql = $"SELECT COALESCE(jsonb_agg(payload ORDER BY payload ->> 'id')::text, '[]') "
            + $"FROM (SELECT {payload} AS payload FROM {tableName} AS row) AS rows";
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private sealed record LegacySnapshotIds(Guid Positive, Guid Zero, Guid Unknown);

    private sealed record LegacyFinancialEvidence(
        string Orders,
        string OrderItems,
        string Snapshots,
        string Units,
        string OwnerLinks,
        string Witnesses,
        string Coverage,
        string Transactions);
}
