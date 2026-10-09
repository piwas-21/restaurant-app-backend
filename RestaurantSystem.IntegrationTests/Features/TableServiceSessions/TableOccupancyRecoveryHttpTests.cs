using System.Data;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

[Collection("Database Lane 4")]
public sealed class TableOccupancyRecoveryHttpTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly MutableTimeProvider _timeProvider = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.PostConfigure<TenantFeatureSettings>(settings =>
        {
            settings.ServerWorkspaceV2 = true;
            settings.TableGuestVisitsV1 = true;
            settings.TableVisitReadinessV1 = true;
        });
    }

    [Fact]
    public async Task Number_only_legacy_visit_can_be_released_then_readied_and_reopened_without_rewriting_history()
    {
        var seeded = await SeedNumberOnlyLegacyVisitAsync();
        AuthenticateAsRole(UserRole.Server);

        var previewResponse = await GetFromJsonAsync<ApiResponse<TableOccupancyRecoveryPreviewDto>>(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery?serviceSessionId={seeded.SessionId}");
        previewResponse!.Success.Should().BeTrue();
        var preview = previewResponse.Data!;
        preview.ServiceSessionId.Should().Be(seeded.SessionId);
        preview.OrderCount.Should().Be(3);
        preview.LegacyUnassignedCount.Should().Be(2);
        preview.CancelableUnsentCount.Should().Be(1);
        preview.RoutedOrderCount.Should().Be(2);
        preview.PaidOrRefundedOrderCount.Should().Be(1);
        preview.ActivePaymentAttemptCount.Should().Be(0);
        preview.PreservedOutstandingAmount.Should().Be(29m);

        var request = new
        {
            operationId = Guid.NewGuid(),
            serviceSessionId = seeded.SessionId,
            expectedReadinessVersion = preview.ReadinessVersion,
            expectedSessionVersion = preview.SessionVersion,
            expectedAccountRevision = preview.AccountRevision,
            previewFingerprint = preview.PreviewFingerprint,
            confirmRecovery = true,
            reason = "Release a verified number-only legacy visit"
        };
        var clockTicks = TimeProvider.System.GetUtcNow().UtcTicks;
        var microsecondTicks = TimeSpan.TicksPerMicrosecond;
        var fixedUtcNow = new DateTimeOffset(
            clockTicks - clockTicks % microsecondTicks + 6, TimeSpan.Zero);
        _timeProvider.SetUtcNow(fixedUtcNow);
        using var recoveryHttp = await PostAsJsonAsync(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery", request);
        recoveryHttp.EnsureSuccessStatusCode();
        var recoveryResponse = await ReadResponseAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(recoveryHttp);
        recoveryResponse!.Success.Should().BeTrue();
        var recovery = recoveryResponse.Data!;
        recovery.CancelledUnsentCount.Should().Be(1);
        recovery.ArchivedLegacyCount.Should().Be(1);
        recovery.RetainedPriorVisitCount.Should().Be(1);
        recovery.PreservedPaidAmount.Should().Be(1m);
        recovery.PreservedOutstandingAmount.Should().Be(29m);
        recovery.VisitReleasedAt.Should().NotBeNull();
        recovery.RecordedAt.Should().Be(fixedUtcNow.UtcDateTime.AddTicks(-6));
        (recovery.RecordedAt.Ticks % microsecondTicks).Should().Be(0);
        recovery.VisitReleasedAt.Should().Be(recovery.RecordedAt);

        var ownerReceipt = await GetFromJsonAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery/operations/{request.operationId}");
        ownerReceipt!.Success.Should().BeTrue();
        ownerReceipt.Data.Should().BeEquivalentTo(recovery);
        AuthenticateAsRole(UserRole.Cashier);
        var privateReceipt = await GetFromJsonAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery/operations/{request.operationId}");
        privateReceipt!.Success.Should().BeFalse();
        privateReceipt.ErrorCode.Should().Be(RestaurantSystem.Api.Common.Models.ErrorCodes
            .TableOccupancyRecoveryOperationNotFound);
        AuthenticateAsRole(UserRole.Server);

        var occupancy = await GetFromJsonAsync<ApiResponse<List<TableDto>>>("/api/Tables/occupancy");
        var physicalTable = occupancy!.Data!.Should().ContainSingle(value => value.Id == seeded.TableId).Which;
        physicalTable.IsOccupied.Should().BeFalse();
        physicalTable.ActiveOrderCount.Should().Be(0);
        physicalTable.ReadinessState.Should().Be(nameof(TableReadinessState.NeedsReset));

        var floor = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var floorTable = floor!.Data!.Tables.Should().ContainSingle(value => value.TableId == seeded.TableId).Which;
        floorTable.HasLegacyAmbiguity.Should().BeFalse();
        floorTable.ActiveRoundCount.Should().Be(0);
        floorTable.PermittedActions.Should().Contain("MarkTableReady");

        using var readyHttp = await PostAsJsonAsync(
            $"/api/Tables/{seeded.TableId}/ready",
            new { operationId = Guid.NewGuid(), expectedReadinessVersion = recovery.ReadinessVersion });
        readyHttp.EnsureSuccessStatusCode();
        (await ReadResponseAsync<ApiResponse<TableReadinessOperationDto>>(readyHttp))!
            .Data!.ReadinessState.Should().Be(nameof(TableReadinessState.ReadyForGuests));

        using var openHttp = await PostAsJsonAsync("/api/table-service-sessions",
            new { tableId = seeded.TableId, currency = "CHF" });
        openHttp.EnsureSuccessStatusCode();
        var opened = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(openHttp))!.Data!;
        opened.TableId.Should().Be(seeded.TableId);
        opened.ServiceSessionId.Should().NotBe(seeded.SessionId);

        var currentVisit = await GetFromJsonAsync<ApiResponse<TableServiceSessionDto>>(
            $"/api/table-service-sessions/{opened.ServiceSessionId}");
        currentVisit!.Data!.LegacyActiveOrderCount.Should().Be(0);
        currentVisit.Data.CanClose.Should().BeTrue();

        AuthenticateAsRole(UserRole.Cashier);
        var cashierQueue = await GetFromJsonAsync<ApiResponse<PagedResult<CashierOrderGroupDto>>>(
            "/api/orders/cashier-groups?scope=Operational&search=OCC-UNIDENTIFIED&pageSize=10");
        cashierQueue!.Success.Should().BeTrue(cashierQueue.Message);
        var unidentified = cashierQueue.Data!.Items.Should().ContainSingle(group =>
            group.Orders.Any(order => order.Id == seeded.UnidentifiedOrderId)).Which;
        unidentified.IsArchivedFromTable.Should().BeFalse();
        unidentified.Orders.Single().RemainingAmount.Should().Be(12m);

        var archivedQueue = await GetFromJsonAsync<ApiResponse<PagedResult<CashierOrderGroupDto>>>(
            "/api/orders/cashier-groups?scope=Operational&search=OCC-LEGACY-ROUND&pageSize=10");
        var archivedLegacy = archivedQueue!.Data!.Items.Should().ContainSingle(group =>
            group.Orders.Any(order => order.Id == seeded.LegacyOrderId)).Which;
        archivedLegacy.IsArchivedFromTable.Should().BeTrue();
        archivedLegacy.Orders.Single().Status.Should().Be(nameof(OrderStatus.Preparing));
        AuthenticateAsRole(UserRole.Server);

        using var replayHttp = await PostAsJsonAsync(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery", request);
        replayHttp.EnsureSuccessStatusCode();
        var replay = (await ReadResponseAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(replayHttp))!.Data!;
        replay.Should().BeEquivalentTo(recovery);

        await using var verify = DatabaseFixture.CreateContext();
        var oldVisit = await verify.TableServiceSessions.SingleAsync(value => value.Id == seeded.SessionId);
        oldVisit.TableId.Should().BeNull("numeric compatibility membership remains historical");
        oldVisit.ReleasedAt.Should().NotBeNull();
        oldVisit.Version.Should().Be(7, "only a physical release advanced the legacy visit");
        oldVisit.AccountRevision.Should().Be(8,
            "cancelling an unassigned legacy order must not stale this visit's frozen account plan");
        var oldOrders = await verify.Orders.Where(value => value.Id == seeded.MemberOrderId
            || value.Id == seeded.LegacyOrderId || value.Id == seeded.LegacyUnsentOrderId).ToListAsync();
        oldOrders.Should().HaveCount(3);
        var member = oldOrders.Single(value => value.Id == seeded.MemberOrderId);
        member.ServiceSessionId.Should().Be(seeded.SessionId);
        member.Status.Should().Be(OrderStatus.Preparing);
        member.IsKitchenReleased.Should().BeTrue();
        var legacyPreparing = oldOrders.Single(value => value.Id == seeded.LegacyOrderId);
        legacyPreparing.ServiceSessionId.Should().BeNull();
        legacyPreparing.Status.Should().Be(OrderStatus.Preparing);
        var legacyUnsent = oldOrders.Single(value => value.Id == seeded.LegacyUnsentOrderId);
        legacyUnsent.ServiceSessionId.Should().BeNull();
        legacyUnsent.Status.Should().Be(OrderStatus.Cancelled);
        legacyUnsent.Total.Should().Be(5m);
        legacyUnsent.TotalPaid.Should().Be(0m);
        (await verify.OrderPayments.CountAsync(value => value.OrderId == legacyUnsent.Id)).Should().Be(0);
        var unidentifiedOrder = await verify.Orders.SingleAsync(value => value.Id == seeded.UnidentifiedOrderId);
        unidentifiedOrder.TableId.Should().BeNull();
        unidentifiedOrder.TableNumber.Should().BeNull();
        unidentifiedOrder.ServiceSessionId.Should().BeNull();
        unidentifiedOrder.Status.Should().Be(OrderStatus.Confirmed);
        unidentifiedOrder.Total.Should().Be(12m);
        unidentifiedOrder.TotalPaid.Should().Be(0m);
        (await verify.TableOccupancyRecoveryDispositions.CountAsync(value =>
            value.OrderId == unidentifiedOrder.Id)).Should().Be(0);
        var frozenPlan = await verify.AccountEqualSharePlans.SingleAsync(value => value.ServiceSessionId == seeded.SessionId);
        frozenPlan.AccountRevision.Should().Be(8);
        frozenPlan.RoundingIncrementMinor.Should().Be(1);
        frozenPlan.InvalidatedAt.Should().BeNull();
        (await verify.TableOccupancyRecoveryOperations.CountAsync()).Should().Be(1);
        (await verify.TableOccupancyRecoveryDispositions.CountAsync()).Should().Be(3);
        var currentSession = await verify.TableServiceSessions.SingleAsync(value => value.Id == opened.ServiceSessionId);
        currentSession.TableId.Should().Be(seeded.TableId);
        currentSession.ReleasedAt.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_table_write_makes_recovery_preview_stale_without_archiving_orders()
    {
        var seeded = await SeedNumberOnlyLegacyVisitAsync();
        AuthenticateAsRole(UserRole.Server);

        var previewResponse = await GetFromJsonAsync<ApiResponse<TableOccupancyRecoveryPreviewDto>>(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery?serviceSessionId={seeded.SessionId}");
        var preview = previewResponse!.Data!;
        var request = new
        {
            operationId = Guid.NewGuid(),
            serviceSessionId = seeded.SessionId,
            expectedReadinessVersion = preview.ReadinessVersion,
            expectedSessionVersion = preview.SessionVersion,
            expectedAccountRevision = preview.AccountRevision,
            previewFingerprint = preview.PreviewFingerprint,
            confirmRecovery = true,
            reason = "Reject a preview after another table writer commits"
        };

        await using var writer = DatabaseFixture.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var table = await writer.Tables.SingleAsync(value => value.Id == seeded.TableId);
        table.ReadinessVersion++;
        await writer.SaveChangesAsync();

        var recoveryTask = PostAsJsonAsync($"/api/Tables/{seeded.TableId}/occupancy-recovery", request);
        var waitedForWriter = await WaitForTableRowLockAsync();
        await transaction.CommitAsync();

        using var response = await recoveryTask.WaitAsync(TimeSpan.FromSeconds(10));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadResponseAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(response);
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableOccupancyRecoveryPreviewStale);
        waitedForWriter.Should().BeTrue("the recovery transaction should overlap the competing table write");

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Tables.SingleAsync(value => value.Id == seeded.TableId)).ReadinessVersion.Should().Be(5);
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == seeded.SessionId)).ReleasedAt.Should().BeNull();
        (await verify.TableOccupancyRecoveryOperations.CountAsync()).Should().Be(0);
        (await verify.TableOccupancyRecoveryDispositions.CountAsync()).Should().Be(0);
        var orders = await verify.Orders.Where(value => value.Id == seeded.LegacyOrderId
            || value.Id == seeded.LegacyUnsentOrderId || value.Id == seeded.UnidentifiedOrderId).ToListAsync();
        orders.Should().HaveCount(3);
        orders.Single(value => value.Id == seeded.LegacyOrderId).Status.Should().Be(OrderStatus.Preparing);
        orders.Single(value => value.Id == seeded.LegacyUnsentOrderId).Status.Should().Be(OrderStatus.Pending);
        orders.Single(value => value.Id == seeded.UnidentifiedOrderId).TableNumber.Should().BeNull();
    }

    [Fact]
    public async Task Captured_payment_from_a_prior_round_does_not_block_cancelling_an_unsent_member_order()
    {
        var seeded = await SeedCapturedVisitWithUnsentOrderAsync();
        AuthenticateAsRole(UserRole.Server);

        var previewResponse = await GetFromJsonAsync<ApiResponse<TableOccupancyRecoveryPreviewDto>>(
            $"/api/Tables/{seeded.TableId}/occupancy-recovery?serviceSessionId={seeded.SessionId}");
        var preview = previewResponse!.Data!;
        preview.ActivePaymentAttemptCount.Should().Be(0,
            "a captured attempt is retained as history, but does not hold a reservation");
        preview.PaidOrRefundedOrderCount.Should().Be(1);
        preview.CancelableUnsentCount.Should().Be(1,
            "a captured prior round must not block an unrelated pending visit member");

        var request = new
        {
            operationId = Guid.NewGuid(),
            serviceSessionId = seeded.SessionId,
            expectedReadinessVersion = preview.ReadinessVersion,
            expectedSessionVersion = preview.SessionVersion,
            expectedAccountRevision = preview.AccountRevision,
            previewFingerprint = preview.PreviewFingerprint,
            confirmRecovery = true,
            reason = "Cancel one verified unsent order after a captured prior round"
        };
        using var response = await PostAsJsonAsync($"/api/Tables/{seeded.TableId}/occupancy-recovery", request);
        response.EnsureSuccessStatusCode();
        var result = (await ReadResponseAsync<ApiResponse<TableOccupancyRecoveryOperationDto>>(response))!.Data!;
        result.CancelledUnsentCount.Should().Be(1);
        result.RetainedPriorVisitCount.Should().Be(1);
        result.PreservedPaidAmount.Should().Be(10m);
        result.PreservedOutstandingAmount.Should().Be(0m);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.SingleAsync(value => value.Id == seeded.PaidOrderId)).Status
            .Should().Be(OrderStatus.Completed);
        (await verify.OrderPayments.SingleAsync(value => value.OrderId == seeded.PaidOrderId)).Amount
            .Should().Be(10m);
        (await verify.AccountPaymentAttempts.SingleAsync(value => value.ServiceSessionId == seeded.SessionId))
            .State.Should().Be(AccountPaymentState.Captured);
        (await verify.Orders.SingleAsync(value => value.Id == seeded.UnsentOrderId)).Status
            .Should().Be(OrderStatus.Cancelled);
        var visit = await verify.TableServiceSessions.SingleAsync(value => value.Id == seeded.SessionId);
        visit.ReleasedAt.Should().NotBeNull();
        visit.Version.Should().Be(2);
        visit.AccountRevision.Should().Be(2);
    }

    private async Task<SeededLegacyVisit> SeedNumberOnlyLegacyVisitAsync()
    {
        var now = DateTime.UtcNow;
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TableNumber = "1",
            MaxGuests = 4,
            ReadinessState = TableReadinessState.NeedsReset,
            ReadinessVersion = 4,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 1,
            TableId = null,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Open,
            Version = 6,
            AccountRevision = 8,
            OpenedAt = now.AddHours(-2),
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var memberOrder = NewOrder("LEGACY-VISIT", now, session.Id, 10m, OrderStatus.Preparing, kitchenReleased: true);
        memberOrder.TotalPaid = 1m;
        memberOrder.RemainingAmount = 9m;
        memberOrder.PaymentStatus = PaymentStatus.PartiallyPaid;
        var capturedPayment = new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = memberOrder.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 1m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = now,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var capturedAttempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = session.AccountRevision,
            AmountMinor = 100,
            Currency = "CHF",
            PayloadHash = new string('b', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = now.AddMinutes(5),
            CompletedAt = now,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var capturedAllocation = new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = capturedAttempt.Id,
            OrderId = memberOrder.Id,
            OrderPaymentId = capturedPayment.Id,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = 100,
            AmountMinor = 100,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var unassignedOrder = NewOrder("LEGACY-ROUND", now.AddMinutes(1), null, 20m,
            OrderStatus.Preparing, kitchenReleased: true);
        var unassignedUnsentOrder = NewOrder("LEGACY-UNSENT", now.AddMinutes(2), null, 5m,
            OrderStatus.Pending, kitchenReleased: false);
        var unidentifiedOrder = NewOrder("UNIDENTIFIED", now.AddMinutes(3), null, 12m,
            OrderStatus.Confirmed, kitchenReleased: false);
        memberOrder.TableNumber = 1;
        unassignedOrder.TableNumber = 1;
        unassignedUnsentOrder.TableNumber = 1;
        var frozenPlan = new AccountEqualSharePlan
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            AccountRevision = session.AccountRevision,
            TotalMinor = 1000,
            ShareCount = 3,
            RoundingIncrementMinor = 1,
            Currency = "CHF",
            PayloadHash = new string('a', 64),
            ScopeJson = "[]",
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        await using var context = DatabaseFixture.CreateContext();
        context.Tables.Add(table);
        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(memberOrder, unassignedOrder, unassignedUnsentOrder, unidentifiedOrder);
        context.OrderPayments.Add(capturedPayment);
        context.AccountPaymentAttempts.Add(capturedAttempt);
        context.AccountPaymentAllocations.Add(capturedAllocation);
        context.AccountEqualSharePlans.Add(frozenPlan);
        await context.SaveChangesAsync();
        return new SeededLegacyVisit(table.Id, session.Id, memberOrder.Id,
            unassignedOrder.Id, unassignedUnsentOrder.Id, unidentifiedOrder.Id);
    }

    private async Task<SeededCapturedVisit> SeedCapturedVisitWithUnsentOrderAsync()
    {
        var now = DateTime.UtcNow;
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TableNumber = "CAP-QA",
            MaxGuests = 4,
            ReadinessState = TableReadinessState.NeedsReset,
            ReadinessVersion = 3,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableId = table.Id,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = now.AddMinutes(-30),
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var paidOrder = NewOrder("CAPTURED", now, session.Id, 10m, OrderStatus.Completed, kitchenReleased: true);
        paidOrder.TableId = table.Id;
        paidOrder.PaymentStatus = PaymentStatus.Completed;
        paidOrder.TotalPaid = 10m;
        paidOrder.RemainingAmount = 0m;
        var payment = new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = paidOrder.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 10m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = now,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = session.AccountRevision,
            AmountMinor = 1000,
            Currency = "CHF",
            PayloadHash = new string('c', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = now.AddMinutes(5),
            CompletedAt = now,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var allocation = new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            OrderId = paidOrder.Id,
            OrderPaymentId = payment.Id,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = 1000,
            AmountMinor = 1000,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };
        var unsentOrder = NewOrder("CAPTURED-UNSENT", now.AddMinutes(1), session.Id, 5m,
            OrderStatus.Pending, kitchenReleased: false);
        unsentOrder.TableId = table.Id;

        await using var context = DatabaseFixture.CreateContext();
        context.Tables.Add(table);
        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(paidOrder, unsentOrder);
        context.OrderPayments.Add(payment);
        context.AccountPaymentAttempts.Add(attempt);
        context.AccountPaymentAllocations.Add(allocation);
        await context.SaveChangesAsync();
        return new SeededCapturedVisit(table.Id, session.Id, paidOrder.Id, unsentOrder.Id);
    }

    private static Order NewOrder(
        string suffix, DateTime now, Guid? sessionId, decimal total, OrderStatus status, bool kitchenReleased) => new()
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"OCC-{suffix}",
            Type = OrderType.DineIn,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            ServiceSessionId = sessionId,
            Total = total,
            RemainingAmount = total,
            TotalPaid = 0,
            IsKitchenReleased = kitchenReleased,
            KitchenReleasedAt = kitchenReleased ? now : null,
            OrderDate = now,
            CreatedBy = nameof(TableOccupancyRecoveryHttpTests)
        };

    private async Task<bool> WaitForTableRowLockAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database()
                    AND wait_event_type = 'Lock'
                    AND query ILIKE '%FOR NO KEY UPDATE%')
            """, connection);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is true) return true;
            await Task.Delay(25);
        }

        return false;
    }

    private sealed record SeededLegacyVisit(
        Guid TableId, Guid SessionId, Guid MemberOrderId, Guid LegacyOrderId,
        Guid LegacyUnsentOrderId, Guid UnidentifiedOrderId);

    private sealed record SeededCapturedVisit(
        Guid TableId, Guid SessionId, Guid PaidOrderId, Guid UnsentOrderId);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private long _utcTicks = TimeProvider.System.GetUtcNow().UtcTicks;

        public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }
}
