using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderBillingSnapshotPersistenceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime SourceCreatedAt = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Database_persists_valid_money_redemption_source_and_erasure_link()
    {
        var source = await SeedSourceAsync();
        var result = Build(source);

        await PersistAsync(result);

        await using var read = fixture.CreateContext();
        var header = await read.OrderBillingSnapshots.AsNoTracking().SingleAsync();
        var unit = await read.OrderBillingSnapshotUnits.AsNoTracking().SingleAsync();
        var link = await read.OrderBillingSnapshotOwnerLinks.AsNoTracking().SingleAsync();
        header.RedemptionTransactionId.Should().Be(source.Transaction.Id);
        header.RedemptionTransactionType.Should().Be(TransactionType.Redeemed);
        header.RedemptionTransactionPoints.Should().Be(-25);
        header.RedemptionTransactionOrderTotal.Should().BeNull();
        header.RedemptionTransactionCreatedAt.Should().Be(SourceCreatedAt);
        header.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
        unit.GrossFoodMinor.Should().Be(100);
        unit.PayableFoodMinor.Should().Be(75);
        link.OrderId.Should().Be(source.Order.Id);
        link.Slot.Should().Be(OrderBillingSnapshotOwnerSlot.Redemption);
        link.UserId.Should().Be(source.UserId);
        link.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Linked);
        link.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);
    }

    [Fact]
    public async Task Database_rejects_owner_link_for_a_different_live_customer()
    {
        var source = await SeedSourceAsync(seedSecondOwner: true);
        var result = Build(source);
        result.OwnerLinks.Single().UserId = source.OtherUserId;

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => PersistAsync(result));

        failure.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Database_rejects_redemption_evidence_that_differs_from_the_persisted_debit()
    {
        var source = await SeedSourceAsync();
        var result = Build(source);
        result.Header.RedemptionTransactionCreatedAt = SourceCreatedAt.AddMinutes(1);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => PersistAsync(result));

        failure.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Deferred_constraints_reject_missing_owner_slot_and_incomplete_units()
    {
        var missingOwner = Build(await SeedSourceAsync());
        var missingOwnerFailure = await Assert.ThrowsAsync<PostgresException>(
            () => PersistAsync(missingOwner, includeOwnerLinks: false));
        missingOwnerFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        var missingUnit = Build(await SeedSourceAsync());
        var missingUnitFailure = await Assert.ThrowsAsync<PostgresException>(
            () => PersistAsync(missingUnit, includeUnits: false));
        missingUnitFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Deferred_constraints_reject_conserved_row_math_when_header_sums_do_not_match()
    {
        var result = Build(await SeedSourceAsync());
        var unit = result.Units.Single();
        unit.PayableFoodMinor++;
        unit.FoodReconciliationMinor++;

        var failure = await Assert.ThrowsAsync<PostgresException>(() => PersistAsync(result));

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Deferred_constraints_reject_a_conserved_snapshot_that_disagrees_with_accepted_order_tip_and_total()
    {
        var source = await SeedSourceAsync();
        var result = Build(source);
        result.Header.TipMinor = 1;
        result.Header.TotalMinor = 76;

        (source.Order.Total * 100).Should().Be(75m, "the database source is the accepted pricing oracle");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => PersistAsync(result));

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Deferred_constraints_reject_locally_conserved_courtesy_that_disagrees_with_native_pricing()
    {
        var result = Build(await SeedSourceAsync());
        result.Header.RawCourtesyRoundingAmount = 0.01m;
        result.Header.CourtesyRoundingMinor = 1;
        result.Header.FoodReconciliationMinor = -1;
        result.Units.Single().CourtesyRoundingMinor = 1;
        result.Units.Single().FoodReconciliationMinor = -1;

        var failure = await Assert.ThrowsAsync<PostgresException>(() => PersistAsync(result));

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Deferred_constraints_reject_a_locally_conserved_food_fee_split_that_disagrees_with_the_order()
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            Type = OrderType.Delivery,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            DeliveryFee = 0.50m,
            Total = 1.50m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test",
            Items = [NewItem(orderId, 1m)]
        };
        await using (var seed = fixture.CreateContext())
        {
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        var result = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);
        result.Header.ChargedDeliveryFeeMinor.Should().Be(50);
        result.Header.PayableFoodMinor.Should().Be(100);
        result.Header.ChargedDeliveryFeeMinor = 0;
        result.Header.PayableFoodMinor = 150;
        result.Header.FoodReconciliationMinor = 50;
        result.Units.Single().PayableFoodMinor = 150;
        result.Units.Single().FoodReconciliationMinor = 50;

        var failure = await Assert.ThrowsAsync<PostgresException>(() => PersistAsync(result));

        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Database_accepts_raw_subcent_discount_when_persisted_component_matches_frozen_minor_value()
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 10m,
            Discount = 1.505m,
            Total = 9m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test",
            Items = [NewItem(orderId, 10m)]
        };

        await using (var context = fixture.CreateContext())
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            order.Discount.Should().Be(1.505m, "the accepted pricing input retains its raw percentage amount");
            var result = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);
            result.Header.RawOrderDiscountAmount.Should().Be(1.505m);
            result.Header.OrderDiscountMinor.Should().Be(151);
            result.Header.RawCourtesyRoundingAmount.Should().Be(0.505m);
            result.Header.CourtesyRoundingMinor.Should().Be(51);
            context.OrderBillingSnapshots.Add(result.Header);
            context.OrderBillingSnapshotUnits.AddRange(result.Units);
            await context.SaveChangesAsync();
        }

        await using var verify = fixture.CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == orderId))
            .Discount.Should().Be(1.51m, "the persisted pricing column uses the same two-decimal minor-unit rounding");
        var header = await verify.OrderBillingSnapshots.AsNoTracking().SingleAsync(candidate => candidate.OrderId == orderId);
        header.RawOrderDiscountAmount.Should().Be(1.505m);
        header.OrderDiscountMinor.Should().Be(151);
        header.TotalMinor.Should().Be(900);
    }

    [Fact]
    public async Task Linked_owner_cannot_be_unlinked_or_reassigned_without_erasure()
    {
        var source = await SeedSourceAsync(seedSecondOwner: true);
        await PersistAsync(Build(source));

        await using var reassign = fixture.CreateContext();
        var reassignFailure = await Assert.ThrowsAsync<PostgresException>(() => reassign.Orders
            .Where(order => order.Id == source.Order.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.UserId, source.OtherUserId)));
        reassignFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        await using var unlink = fixture.CreateContext();
        var unlinkFailure = await Assert.ThrowsAsync<PostgresException>(() => unlink.Orders
            .Where(order => order.Id == source.Order.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.UserId, (Guid?)null)));
        unlinkFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Snapshot_without_an_evaluated_owner_slot_does_not_freeze_guest_reassignment()
    {
        var (order, nextOwnerId) = await SeedUnredeemedOrderAsync();
        var result = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);
        result.OwnerLinks.Should().BeEmpty();
        await PersistAsync(result);

        await using var update = fixture.CreateContext();
        var updated = await update.Orders.Where(candidate => candidate.Id == order.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.UserId, nextOwnerId));

        updated.Should().Be(1);
    }

    [Fact]
    public async Task Evaluated_zero_candidate_still_persists_a_required_earning_owner_slot()
    {
        var (order, _) = await SeedUnredeemedOrderAsync();
        var result = OrderBillingSnapshotFactory.Build(order, "CHF",
            new OrderBillingEarningEvaluation(0, "fixed-priority-v1", new string('a', 64), null), null, 1_000);

        result.Header.EarnedPointsCandidate.Should().Be(0);
        result.OwnerLinks.Should().ContainSingle().Which.Slot.Should().Be(OrderBillingSnapshotOwnerSlot.Earning);
        await PersistAsync(result);

        await using var verify = fixture.CreateContext();
        var link = await verify.OrderBillingSnapshotOwnerLinks.AsNoTracking().SingleAsync();
        link.UserId.Should().Be(order.UserId);
        link.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Linked);
    }

    [Fact]
    public async Task Referenced_item_and_redemption_facts_cannot_be_changed_or_deleted_while_owner_lives()
    {
        var source = await SeedSourceAsync();
        await PersistAsync(Build(source));

        await using (var itemUpdate = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => itemUpdate.OrderItems
                .Where(item => item.Id == source.Item.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ItemTotal, 1.01m)));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var sourceUpdate = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => sourceUpdate.FidelityPointsTransactions
                .Where(transaction => transaction.Id == source.Transaction.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(transaction => transaction.Points, -24)));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var sourceDelete = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => sourceDelete.FidelityPointsTransactions
                .Where(transaction => transaction.Id == source.Transaction.Id)
                .ExecuteDeleteAsync());
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using var verify = fixture.CreateContext();
        (await verify.FidelityPointsTransactions.AnyAsync(transaction => transaction.Id == source.Transaction.Id))
            .Should().BeTrue("a standalone debit deletion must roll back while its owner still exists");
    }

    [Fact]
    public async Task Accepted_order_billing_fields_freeze_while_payment_summary_and_operational_text_remain_writable()
    {
        var source = await SeedSourceAsync();
        await PersistAsync(Build(source));

        await using (var pricingUpdate = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => pricingUpdate.Orders
                .Where(order => order.Id == source.Order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Total, 1m)));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var operationalUpdate = fixture.CreateContext())
        {
            var updated = await operationalUpdate.Orders.Where(order => order.Id == source.Order.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.TotalPaid, 0.25m)
                    .SetProperty(order => order.RemainingAmount, 0.50m)
                    .SetProperty(order => order.PaymentStatus, PaymentStatus.PartiallyPaid)
                    .SetProperty(order => order.Notes, "Payment follow-up"));
            updated.Should().Be(1);
        }

        await using (var itemTextUpdate = fixture.CreateContext())
        {
            var updated = await itemTextUpdate.OrderItems.Where(item => item.Id == source.Item.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ProductName, "Updated kitchen label"));
            updated.Should().Be(1);
        }

        await using var verify = fixture.CreateContext();
        var order = await verify.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == source.Order.Id);
        order.Total.Should().Be(0.75m);
        order.TotalPaid.Should().Be(0.25m);
        order.RemainingAmount.Should().Be(0.50m);
        order.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        order.Notes.Should().Be("Payment follow-up");
        (await verify.OrderItems.AsNoTracking().SingleAsync(item => item.Id == source.Item.Id))
            .ProductName.Should().Be("Updated kitchen label");
    }

    [Fact]
    public async Task Snapshotted_item_graph_rejects_add_remove_reparent_and_charge_changes()
    {
        var source = await SeedSourceAsync();
        var child = NewItem(source.Order.Id, 0m, source.Item.Id);
        await using (var seedChild = fixture.CreateContext())
        {
            seedChild.OrderItems.Add(child);
            await seedChild.SaveChangesAsync();
        }
        await PersistAsync(Build(source));

        await using (var rootInsert = fixture.CreateContext())
        {
            rootInsert.OrderItems.Add(NewItem(source.Order.Id, 0m));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => rootInsert.SaveChangesAsync());
            failure.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var childInsert = fixture.CreateContext())
        {
            childInsert.OrderItems.Add(NewItem(source.Order.Id, 0m, source.Item.Id));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => childInsert.SaveChangesAsync());
            failure.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var rootDelete = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => rootDelete.OrderItems
                .Where(item => item.Id == source.Item.Id).ExecuteDeleteAsync());
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var childDelete = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => childDelete.OrderItems
                .Where(item => item.Id == child.Id).ExecuteDeleteAsync());
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var rootQuantityUpdate = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => rootQuantityUpdate.OrderItems
                .Where(item => item.Id == source.Item.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, 2)));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var childReparent = fixture.CreateContext())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => childReparent.OrderItems
                .Where(item => item.Id == child.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentOrderItemId, (Guid?)null)));
            failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        var (unfrozenOrder, _) = await SeedUnredeemedOrderAsync();
        await using var moveIntoFrozen = fixture.CreateContext();
        var moveFailure = await Assert.ThrowsAsync<PostgresException>(() => moveIntoFrozen.OrderItems
            .Where(item => item.OrderId == unfrozenOrder.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.OrderId, source.Order.Id)));
        moveFailure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation,
            "the guard must check the destination order as well as the original order");
    }

    [Fact]
    public async Task Initial_order_items_can_be_created_before_the_snapshot_in_the_same_transaction()
    {
        var orderId = Guid.NewGuid();
        var root = NewItem(orderId, 1m);
        var child = NewItem(orderId, 0m, root.Id);
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            Total = 1m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test",
            Items = [root, child]
        };

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var result = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);
        context.OrderBillingSnapshots.Add(result.Header);
        context.OrderBillingSnapshotUnits.AddRange(result.Units);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var verify = fixture.CreateContext();
        (await verify.OrderItems.CountAsync(item => item.OrderId == orderId)).Should().Be(2);
        (await verify.OrderBillingSnapshotUnits.CountAsync(unit => unit.OrderId == orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Unsnapshotted_legacy_order_items_remain_insertable_and_deletable()
    {
        var (order, _) = await SeedUnredeemedOrderAsync();
        var extra = NewItem(order.Id, 1m);

        await using (var add = fixture.CreateContext())
        {
            add.OrderItems.Add(extra);
            await add.SaveChangesAsync();
        }

        await using (var remove = fixture.CreateContext())
        {
            var deleted = await remove.OrderItems.Where(item => item.Id == extra.Id).ExecuteDeleteAsync();
            deleted.Should().Be(1);
        }
    }

    [Fact]
    public async Task Snapshot_parent_lock_serializes_a_concurrent_item_insert()
    {
        var (order, _) = await SeedUnredeemedOrderAsync();
        var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);

        await using var capture = fixture.CreateContext();
        await capture.Database.OpenConnectionAsync();
        await using var captureTransaction = await capture.Database.BeginTransactionAsync();
        capture.OrderBillingSnapshots.Add(snapshot.Header);
        capture.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        await capture.SaveChangesAsync();
        var captureProcessId = ((NpgsqlConnection)capture.Database.GetDbConnection()).ProcessID;

        await using var mutation = fixture.CreateContext();
        await mutation.Database.OpenConnectionAsync();
        var mutationProcessId = ((NpgsqlConnection)mutation.Database.GetDbConnection()).ProcessID;
        mutation.OrderItems.Add(NewItem(order.Id, 0m));
        var insertTask = mutation.SaveChangesAsync();
        var blockedByCapture = await WaitForBlockedSessionAsync(captureProcessId, mutationProcessId);
        if (!blockedByCapture)
        {
            await captureTransaction.RollbackAsync();
            await CaptureExceptionAsync(insertTask);
            blockedByCapture.Should().BeTrue("the snapshot header must serialize graph inserts on its parent Order");
            return;
        }

        await captureTransaction.CommitAsync();
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => insertTask);
        failure.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        await using var verify = fixture.CreateContext();
        (await verify.OrderItems.CountAsync(item => item.OrderId == order.Id)).Should().Be(1);
        (await verify.OrderBillingSnapshotUnits.CountAsync(unit => unit.OrderId == order.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_parent_lock_serializes_a_concurrent_accepted_quantity_update()
    {
        var (order, _) = await SeedUnredeemedOrderAsync();
        var itemId = order.Items.Single().Id;
        var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 1_000);

        await using var capture = fixture.CreateContext();
        await capture.Database.OpenConnectionAsync();
        await using var captureTransaction = await capture.Database.BeginTransactionAsync();
        capture.OrderBillingSnapshots.Add(snapshot.Header);
        capture.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        await capture.SaveChangesAsync();
        var captureProcessId = ((NpgsqlConnection)capture.Database.GetDbConnection()).ProcessID;

        await using var mutation = fixture.CreateContext();
        await mutation.Database.OpenConnectionAsync();
        var mutationProcessId = ((NpgsqlConnection)mutation.Database.GetDbConnection()).ProcessID;
        var updateTask = mutation.OrderItems.Where(item => item.Id == itemId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, 2));
        var blockedByCapture = await WaitForBlockedSessionAsync(captureProcessId, mutationProcessId);
        if (!blockedByCapture)
        {
            await captureTransaction.RollbackAsync();
            await CaptureExceptionAsync(updateTask);
            blockedByCapture.Should().BeTrue("accepted graph updates must serialize on their parent Order");
            return;
        }

        await captureTransaction.CommitAsync();
        var failure = await CaptureExceptionAsync(updateTask);
        failure.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        await using var verify = fixture.CreateContext();
        (await verify.OrderItems.AsNoTracking().SingleAsync(item => item.Id == itemId)).Quantity.Should().Be(1);
        (await verify.OrderBillingSnapshotUnits.CountAsync(unit => unit.OrderId == order.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Item_graph_trigger_refuses_missing_and_uncommitted_parent_orders()
    {
        var missingOrderId = Guid.NewGuid();
        await using (var missingParent = fixture.CreateContext())
        {
            missingParent.OrderItems.Add(NewItem(missingOrderId, 0m));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => missingParent.SaveChangesAsync());
            failure.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        var pendingOrderId = Guid.NewGuid();
        await using var pendingParent = fixture.CreateContext();
        await pendingParent.Database.OpenConnectionAsync();
        await using var pendingTransaction = await pendingParent.Database.BeginTransactionAsync();
        pendingParent.Orders.Add(new Order
        {
            Id = pendingOrderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test"
        });
        await pendingParent.SaveChangesAsync();

        await using var concurrentItem = fixture.CreateContext();
        await concurrentItem.Database.OpenConnectionAsync();
        concurrentItem.OrderItems.Add(NewItem(pendingOrderId, 0m));
        var insertTask = concurrentItem.SaveChangesAsync();
        var completedBeforeParentRelease = await Task.WhenAny(insertTask, Task.Delay(TimeSpan.FromSeconds(2))) == insertTask;
        if (!completedBeforeParentRelease)
            await pendingTransaction.RollbackAsync();

        var uncommittedParentFailure = await CaptureExceptionAsync(insertTask);
        completedBeforeParentRelease.Should().BeTrue(
            "another transaction's uncommitted parent must be refused immediately instead of waited on");
        uncommittedParentFailure.Should().BeOfType<DbUpdateException>()
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        if (completedBeforeParentRelease)
            await pendingTransaction.RollbackAsync();

        await using var verify = fixture.CreateContext();
        (await verify.OrderItems.CountAsync(item => item.OrderId == pendingOrderId)).Should().Be(0);
    }

    private async Task<SourceFixture> SeedSourceAsync(bool seedSecondOwner = false)
    {
        var userId = Guid.NewGuid();
        var otherUserId = seedSecondOwner ? Guid.NewGuid() : (Guid?)null;
        var orderId = Guid.NewGuid();
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Snapshot dish",
            Quantity = 1,
            UnitPrice = 1m,
            ItemTotal = 1m,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            Total = 0.75m,
            RemainingAmount = 0.75m,
            FidelityPointsRedeemed = 25,
            FidelityPointsDiscount = 0.25m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test",
            Items = [item]
        };
        var transaction = new FidelityPointsTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = orderId,
            TransactionType = TransactionType.Redeemed,
            Points = -25,
            OrderTotal = null,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test"
        };

        await using var context = fixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, userId);
        if (otherUserId.HasValue)
            await TestUserSeeder.SeedUserAsync(context, otherUserId.Value);
        context.Orders.Add(order);
        context.FidelityPointsTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return new(order, item, transaction, userId, otherUserId);
    }

    private async Task<(Order Order, Guid NextOwnerId)> SeedUnredeemedOrderAsync()
    {
        var userId = Guid.NewGuid();
        var nextOwnerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Guest account claim dish",
            Quantity = 1,
            UnitPrice = 1m,
            ItemTotal = 1m,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SNAP-{Guid.NewGuid():N}"[..17],
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            Total = 1m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-test",
            Items = [item]
        };

        await using var context = fixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, userId);
        await TestUserSeeder.SeedUserAsync(context, nextOwnerId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order, nextOwnerId);
    }

    private static OrderBillingSnapshotBuildResult Build(SourceFixture source) =>
        OrderBillingSnapshotFactory.Build(source.Order, "CHF", null,
            new OrderBillingRedemptionEvidence(source.Transaction.Id, source.UserId, source.Order.Id,
                TransactionType.Redeemed, -25, 0.25m, null, SourceCreatedAt), 1_000);

    private static OrderItem NewItem(Guid orderId, decimal itemTotal, Guid? parentOrderItemId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ParentOrderItemId = parentOrderItemId,
        ProductName = "Snapshot graph test item",
        Quantity = 1,
        UnitPrice = itemTotal,
        ItemTotal = itemTotal,
        CreatedAt = SourceCreatedAt,
        CreatedBy = "snapshot-test"
    };

    private async Task PersistAsync(
        OrderBillingSnapshotBuildResult result,
        bool includeUnits = true,
        bool includeOwnerLinks = true)
    {
        await using var context = fixture.CreateContext();
        context.OrderBillingSnapshots.Add(result.Header);
        if (includeUnits)
            context.OrderBillingSnapshotUnits.AddRange(result.Units);
        if (includeOwnerLinks)
            context.OrderBillingSnapshotOwnerLinks.AddRange(result.OwnerLinks);
        await context.SaveChangesAsync();
    }

    private async Task<bool> WaitForBlockedSessionAsync(int blockingProcessId, int blockedProcessId)
    {
        await using var observer = new NpgsqlConnection(fixture.ConnectionString);
        await observer.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity "
            + "WHERE datname = current_database() AND pid = @blocked_pid AND wait_event_type = 'Lock' "
            + "AND @blocking_pid = ANY(pg_blocking_pids(pid)))", observer);
        command.Parameters.AddWithValue("blocking_pid", blockingProcessId);
        command.Parameters.AddWithValue("blocked_pid", blockedProcessId);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if ((bool)(await command.ExecuteScalarAsync())!)
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed record SourceFixture(
        Order Order,
        OrderItem Item,
        FidelityPointsTransaction Transaction,
        Guid UserId,
        Guid? OtherUserId);
}
