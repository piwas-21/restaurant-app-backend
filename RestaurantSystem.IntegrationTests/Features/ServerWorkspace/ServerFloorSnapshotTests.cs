using System.Data.Common;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FloorPlanEntity = RestaurantSystem.Domain.Entities.FloorPlan;

namespace RestaurantSystem.IntegrationTests.Features.ServerWorkspace;

[Collection("Database Lane 1")]
public sealed class ServerFloorSnapshotTests : IntegrationTestBase
{
    private Guid _stableTableId;
    private Guid _numericTableId;
    private Guid _leadingZeroTableId;
    private Guid _futureReservedTableId;
    private Guid _legacyOnlyTableId;
    private Guid _sessionId;
    private readonly MutableTimeProvider _serverTime = new(DateTimeOffset.UtcNow);
    private readonly MutableClock _tenantClock = new(DateTimeOffset.UtcNow, "America/New_York");

    public ServerFloorSnapshotTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_serverTime);
        services.AddSingleton<ITenantClock>(_tenantClock);
    }

    [Fact]
    public async Task Snapshot_is_authoritative_for_alphanumeric_visit_balance_reservation_and_actions()
    {
        AuthenticateAsRole(UserRole.Server);

        var response = await Client.GetAsync("/api/staff/server-workspace/floor");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<ServerFloorSnapshotDto>>(response);
        var snapshot = body!.Data!;
        var table = snapshot.Tables.Single(item => item.TableId == _stableTableId);

        snapshot.ServerTime.Should().NotBe(default);
        snapshot.TenantTime.Should().NotBe(default);
        snapshot.Version.Should().NotBeNullOrWhiteSpace();
        snapshot.Cursor.Should().Be(snapshot.Version);
        snapshot.Tables.Should().HaveCountGreaterThan(100);
        snapshot.Tables.Count(table => table.Legacy?.ActiveOrderCount == 1)
            .Should().BeGreaterThan(100, "the floor read must remain useful with many live tables");
        table.TableLabel.Should().Be("T-QA");
        table.State.Should().Be("Ready");
        table.ReadyRoundCount.Should().Be(1);
        table.Session!.ServiceSessionId.Should().Be(_sessionId);
        table.Session.Total.Should().Be(25m);
        table.Session.Remaining.Should().Be(25m);
        table.Session.CanCollect.Should().BeTrue();
        table.HasLegacyAmbiguity.Should().BeTrue();
        table.Legacy!.OrderCount.Should().Be(1);
        table.Session.CanClose.Should().BeFalse();
        table.Reservation.Should().NotBeNull();
        table.PermittedActions.Should().Contain(["AddRound", "ViewBill", "OpenTasks"]);
        table.PermittedActions.Should().NotContain("CollectPayment",
            "Server tender entry is not an incidental consequence of floor access");
        table.PermittedActions.Should().Contain("ReviewLegacy");

        var futureReserved = snapshot.Tables.Single(item => item.TableId == _futureReservedTableId);
        futureReserved.Reservation.Should().NotBeNull();
        futureReserved.Reservation!.IsCurrent.Should().BeFalse();
        futureReserved.State.Should().Be("Available",
            "tomorrow's reservation is context, not a claim that the table is occupied now");
        futureReserved.PermittedActions.Should().Contain("StartTable");
    }

    [Fact]
    public async Task Snapshot_maps_legacy_numeric_session_only_to_the_canonical_label()
    {
        AuthenticateAsAdmin();

        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var numeric = body!.Data!.Tables.Single(table => table.TableId == _numericTableId);
        var leadingZero = body.Data.Tables.Single(table => table.TableId == _leadingZeroTableId);

        numeric.Session.Should().NotBeNull();
        leadingZero.Session.Should().BeNull();
        leadingZero.State.Should().Be("Available");
        leadingZero.PermittedActions.Should().Contain("StartTable");
    }

    [Fact]
    public async Task Snapshot_uses_tenant_currency_when_a_historical_session_has_no_currency()
    {
        AuthenticateAsAdmin();
        var before = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var info = await context.RestaurantInfo.SingleAsync();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _sessionId);
        var originalTenantCurrency = info.Currency;
        var originalSessionCurrency = session.Currency;
        try
        {
            info.Currency = "EUR";
            session.Currency = null;
            await context.SaveChangesAsync();

            var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
                "/api/staff/server-workspace/floor");
            var table = body!.Data!.Tables.Single(table => table.TableId == _stableTableId);
            table
                .Session!.Currency.Should().Be("EUR");
            body.Data.Version.Should().NotBe(before!.Data!.Version,
                "effective session currency is emitted by the snapshot");
        }
        finally
        {
            info.Currency = originalTenantCurrency;
            session.Currency = originalSessionCurrency;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Cashier_snapshot_actions_include_collection_but_server_actions_do_not()
    {
        AuthenticateAsRole(UserRole.Cashier);
        var cashier = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        cashier!.Data!.Tables.Single(table => table.TableId == _stableTableId)
            .PermittedActions.Should().Contain("CollectPayment");

        AuthenticateAsRole(UserRole.Server);
        var server = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        server!.Data!.Tables.Single(table => table.TableId == _stableTableId)
            .PermittedActions.Should().NotContain("CollectPayment");
    }

    [Fact]
    public async Task Snapshot_settlement_summary_matches_shared_bill_eligibility_for_terminal_and_unresolved_rounds()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var table = await context.Tables.SingleAsync(value => value.Id == _stableTableId);
            context.Orders.AddRange(
                NewSessionOrder(table, _sessionId, "SNAP-REFUNDED", OrderStatus.Ready,
                    PaymentStatus.Refunded, 10m, 0m, 10m,
                    new OrderPayment
                    {
                        PaymentMethod = PaymentMethod.Cash,
                        Amount = 10m,
                        Status = PaymentStatus.Refunded,
                        IsRefunded = true,
                        RefundedAmount = 10m,
                        PaymentDate = DateTime.UtcNow,
                        CreatedBy = "test"
                    }),
                NewSessionOrder(table, _sessionId, "SNAP-PROCESSING", OrderStatus.Ready,
                    PaymentStatus.Processing, 8m, 0m, 8m,
                    new OrderPayment
                    {
                        PaymentMethod = PaymentMethod.OnlinePayment,
                        Amount = 8m,
                        Status = PaymentStatus.Processing,
                        PaymentDate = DateTime.UtcNow,
                        CreatedBy = "test"
                    }),
                NewSessionOrder(table, _sessionId, "SNAP-OVERPAID", OrderStatus.Completed,
                    PaymentStatus.Overpaid, 5m, 6m, -1m,
                    new OrderPayment
                    {
                        PaymentMethod = PaymentMethod.Cash,
                        Amount = 6m,
                        Status = PaymentStatus.Completed,
                        PaymentDate = DateTime.UtcNow,
                        CreatedBy = "test"
                    }),
                NewSessionOrder(table, _sessionId, "SNAP-COMP-UNPAID", OrderStatus.Completed,
                    PaymentStatus.Pending, 9m, 0m, 9m),
                NewSessionOrder(table, _sessionId, "SNAP-CANCEL", OrderStatus.Cancelled,
                    PaymentStatus.Pending, 100m, 0m, 100m),
                NewSessionOrder(table, _sessionId, "SNAP-UNRESOLVED", OrderStatus.Preparing,
                    PaymentStatus.Pending, 7m, 0m, 7m));
            await context.SaveChangesAsync();

            var bill = await scope.ServiceProvider.GetRequiredService<ITableBillAssembler>()
                .AssembleAsync(_sessionId, CancellationToken.None);
            bill!.Rounds.Single(round => round.Order.OrderNumber == "SNAP-REFUNDED")
                .CanCollect.Should().BeFalse();
            bill.Rounds.Single(round => round.Order.OrderNumber == "SNAP-PROCESSING")
                .CanCollect.Should().BeFalse();
            bill.Rounds.Single(round => round.Order.OrderNumber == "SNAP-OVERPAID")
                .CanCollect.Should().BeFalse();
            bill.Rounds.Single(round => round.Order.OrderNumber == "SNAP-COMP-UNPAID")
                .CanCollect.Should().BeTrue();
            bill.Rounds.Should().NotContain(round => round.Order.OrderNumber == "SNAP-CANCEL");
        }

        AuthenticateAsAdmin();
        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var session = body!.Data!.Tables.Single(table => table.TableId == _stableTableId).Session!;
        session.CanCollect.Should().BeTrue();
        session.CanClose.Should().BeFalse();
        session.ActiveRoundCount.Should().Be(4);
    }

    [Fact]
    public async Task Legacy_orders_keep_a_table_occupied_and_never_offer_start_table()
    {
        AuthenticateAsAdmin();

        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var table = body!.Data!.Tables.Single(item => item.TableId == _legacyOnlyTableId);

        table.Session.Should().BeNull();
        table.State.Should().Be("Ambiguous");
        table.Legacy.Should().NotBeNull();
        table.Legacy!.OrderCount.Should().Be(2);
        table.Legacy.ActiveOrderCount.Should().Be(1);
        table.Legacy.ReadyOrderCount.Should().Be(0);
        table.HasLegacyAmbiguity.Should().BeTrue();
        table.PermittedActions.Should().Contain("ReviewLegacy");
        table.PermittedActions.Should().NotContain("StartTable");
    }

    [Fact]
    public async Task Snapshot_without_reservations_returns_null_next_state_boundary()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Reservations.RemoveRange(await context.Reservations.ToListAsync());
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");

        body!.Data!.NextStateChangeAt.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_projects_combined_reservation_on_primary_and_child_tables()
    {
        Guid childTableId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await context.Reservations.SingleAsync(
                value => value.TableId == _stableTableId);
            childTableId = await context.Tables
                .Where(value => value.TableNumber == "SNAP-1")
                .Select(value => value.Id)
                .SingleAsync();
            reservation.CombinedTables.Add(new ReservationTable
            {
                TableId = childTableId,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var primary = body!.Data!.Tables.Single(table => table.TableId == _stableTableId);
        var child = body.Data.Tables.Single(table => table.TableId == childTableId);

        primary.Reservation.Should().NotBeNull();
        child.Reservation.Should().NotBeNull();
        child.Reservation!.ReservationId.Should().Be(primary.Reservation!.ReservationId);
    }

    [Fact]
    public async Task Snapshot_uses_tenant_wall_clock_for_non_utc_reservation_day_boundary()
    {
        var instant = new DateTimeOffset(2026, 9, 22, 0, 30, 0, TimeSpan.Zero);
        _serverTime.Current = instant;
        _tenantClock.Set(instant, "America/New_York");
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await context.Reservations.SingleAsync(
                value => value.TableId == _futureReservedTableId);
            reservation.ReservationDate = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
            reservation.StartTime = TimeSpan.FromHours(21);
            reservation.EndTime = TimeSpan.FromHours(22);
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var before = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        before!.Data!.NextStateChangeAt.Should().Be(new DateTimeOffset(2026, 9, 22, 1, 0, 0, TimeSpan.Zero));
        before.Data.Tables.Single(table => table.TableId == _futureReservedTableId)
            .Reservation!.IsCurrent.Should().BeFalse();

        _serverTime.Current = instant.AddMinutes(60);
        var after = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        after!.Data!.Tables.Single(table => table.TableId == _futureReservedTableId)
            .Reservation!.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Snapshot_does_not_project_large_terminal_legacy_history()
    {
        AuthenticateAsAdmin();
        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var table = body!.Data!.Tables.Single(item => item.TableId == _legacyOnlyTableId);

        table.Legacy!.OrderCount.Should().Be(2,
            "cancelled and refunded terminal history is outside the operational legacy read");
        table.Legacy.ActiveOrderCount.Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_version_is_stable_until_authoritative_state_changes()
    {
        AuthenticateAsAdmin();
        var first = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var second = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        second!.Data!.Version.Should().Be(first!.Data!.Version);
        second.Data.Cursor.Should().Be(first.Data.Cursor);

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _sessionId);
            session.Version++;
            await context.SaveChangesAsync();
        }

        var changed = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        changed!.Data!.Version.Should().NotBe(first.Data.Version);
        changed.Data.Cursor.Should().Be(changed.Data.Version);
    }

    [Fact]
    public async Task Snapshot_clock_tick_updates_derived_age_without_changing_snapshot_cursor()
    {
        AuthenticateAsAdmin();
        var first = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var firstTable = first!.Data!.Tables.Single(table => table.TableId == _stableTableId);

        _serverTime.Current = _serverTime.Current.AddMinutes(15);
        var second = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        var secondTable = second!.Data!.Tables.Single(table => table.TableId == _stableTableId);

        second.Data.Version.Should().Be(first.Data.Version);
        second.Data.Cursor.Should().Be(first.Data.Cursor);
        secondTable.Session!.AgeMinutes.Should().BeGreaterThan(firstTable.Session!.AgeMinutes);
    }

    [Fact]
    public async Task Snapshot_version_changes_when_returned_reservation_context_changes()
    {
        AuthenticateAsAdmin();
        var first = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await context.Reservations.SingleAsync(
                value => value.TableId == _stableTableId);
            reservation.EndTime = reservation.EndTime.Add(TimeSpan.FromMinutes(30));
            reservation.NumberOfGuests++;
            await context.SaveChangesAsync();
        }

        var changed = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        changed!.Data!.Version.Should().NotBe(first!.Data!.Version);
        changed.Data.Cursor.Should().Be(changed.Data.Version);
    }

    [Fact]
    public async Task Snapshot_version_changes_at_reservation_boundary_without_request_churn()
    {
        AuthenticateAsAdmin();
        DateTimeOffset current;
        using (var scope = Factory.Services.CreateScope())
        {
            var clock = scope.ServiceProvider.GetRequiredService<ITenantClock>();
            current = clock.ToTenantTime(_serverTime.GetUtcNow().UtcDateTime);
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                TableId = _futureReservedTableId,
                CustomerName = "Boundary guest",
                CustomerEmail = TestEmail("boundary"),
                ReservationDate = DateOnly.FromDateTime(current.DateTime)
                    .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                StartTime = current.TimeOfDay.Add(TimeSpan.FromMinutes(2)),
                EndTime = current.TimeOfDay.Add(TimeSpan.FromMinutes(12)),
                NumberOfGuests = 2,
                Status = ReservationStatus.Confirmed,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        var before = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        before!.Data!.NextStateChangeAt.Should().NotBeNull();
        before.Data.Tables.Single(table => table.TableId == _futureReservedTableId)
            .Reservation!.IsCurrent.Should().BeFalse();

        _serverTime.Current = current.ToUniversalTime().AddMinutes(3);
        var after = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>(
            "/api/staff/server-workspace/floor");
        after!.Data!.Version.Should().NotBe(before.Data.Version);
        after.Data.Tables.Single(table => table.TableId == _futureReservedTableId)
            .Reservation!.IsCurrent.Should().BeTrue();
        var currentTable = after.Data.Tables.Single(table => table.TableId == _futureReservedTableId);
        currentTable.State.Should().Be("Reserved");
        currentTable.PermittedActions.Should().BeEmpty(
            "a currently reserved table without a checked-in session must not offer StartTable");
    }

    [Fact]
    public async Task Snapshot_reads_all_state_from_one_repeatable_read_snapshot()
    {
        var gate = new TablesReadGate();
        await using var readerContext = DatabaseFixture.CreateContext(gate);
        using var scope = Factory.Services.CreateScope();
        var reader = new ServerFloorSnapshotReader(
            readerContext,
            scope.ServiceProvider.GetRequiredService<ITenantClock>(),
            new TestCurrentUser(UserRole.Admin),
            new TableBillAssembler(
                readerContext,
                scope.ServiceProvider.GetRequiredService<IOrderMappingService>(),
                scope.ServiceProvider.GetRequiredService<ILogger<TableBillAssembler>>()),
            _serverTime);
        var readTask = reader.ReadAsync(CancellationToken.None);
        await gate.TablesRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var writerContext = DatabaseFixture.CreateContext())
        {
            var order = await writerContext.Orders.SingleAsync(value => value.ServiceSessionId == _sessionId);
            order.Total = 5m;
            order.RemainingAmount = 5m;
            await writerContext.SaveChangesAsync();
        }

        gate.Release.TrySetResult(true);
        var snapshot = await readTask;
        snapshot.Tables.Single(table => table.TableId == _stableTableId)
            .Session!.Remaining.Should().Be(25m);
    }

    [Fact]
    public async Task Snapshot_respects_the_configured_reservation_look_ahead()
    {
        _serverTime.Current = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        await using var context = DatabaseFixture.CreateContext();
        var localDate = DateOnly.FromDateTime(
            _tenantClock.ToTenantTime(_serverTime.GetUtcNow().UtcDateTime).DateTime);
        context.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(),
            TableId = _futureReservedTableId,
            CustomerName = "Configured horizon guest",
            CustomerEmail = TestEmail("horizon"),
            ReservationDate = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            StartTime = TimeSpan.FromHours(12),
            EndTime = TimeSpan.FromHours(13),
            NumberOfGuests = 2,
            Status = ReservationStatus.Confirmed,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();
        var reader = new ServerFloorSnapshotReader(
            context,
            _tenantClock,
            new TestCurrentUser(UserRole.Admin),
            new TableBillAssembler(
                context,
                scope.ServiceProvider.GetRequiredService<IOrderMappingService>(),
                scope.ServiceProvider.GetRequiredService<ILogger<TableBillAssembler>>()),
            _serverTime,
            Options.Create(new TableServiceSessionSettings { FloorReservationLookAheadDays = 1 }));

        var snapshot = await reader.ReadAsync(CancellationToken.None);

        snapshot.Tables.Single(table => table.TableId == _futureReservedTableId)
            .Reservation.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_requires_table_service_staff()
    {
        AuthenticateAsRole(UserRole.KitchenStaff);
        (await Client.GetAsync("/api/staff/server-workspace/floor"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsAnonymous();
        (await Client.GetAsync("/api/staff/server-workspace/floor"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = new FloorPlanEntity
        {
            Id = Guid.NewGuid(),
            Name = "Main floor",
            WidthMeters = 20m,
            HeightMeters = 12m,
            IsDefault = true,
            CreatedBy = "test"
        };
        context.FloorPlans.Add(plan);
        var tables = Enumerable.Range(1, 101).Select(index => NewTable(
            $"SNAP-{index}", plan.Id)).ToList();
        var stable = NewTable("T-QA", plan.Id);
        var futureReserved = NewTable("T-FUTURE", plan.Id);
        var legacyOnly = NewTable("T-LEGACY", plan.Id);
        var numeric = NewTable("7", plan.Id);
        var leadingZero = NewTable("007", plan.Id);
        tables.AddRange([stable, futureReserved, legacyOnly, numeric, leadingZero]);
        context.Tables.AddRange(tables);

        var stableSession = NewSession(stable.Id, null);
        var legacySession = NewSession(null, 7);
        context.TableServiceSessions.AddRange(stableSession, legacySession);
        context.Orders.Add(NewOrder(stable, stableSession.Id));
        context.Orders.Add(NewLegacyOrder(stable, "SNAP-LEGACY", OrderStatus.Completed, 9m));
        context.Orders.Add(NewLegacyOrder(legacyOnly, "SNAP-LEGACY-ACTIVE", OrderStatus.Preparing, 12m));
        context.Orders.Add(NewLegacyOrder(legacyOnly, "SNAP-LEGACY-PAID", OrderStatus.Completed, 7m));
        context.Orders.AddRange(tables.Take(101).Select((table, index) =>
            NewLegacyOrder(table, $"LIVE-{index}", OrderStatus.Preparing, 3m)));
        context.Orders.AddRange(Enumerable.Range(0, 500).Select(index =>
            NewLegacyOrder(legacyOnly, $"TERM-{index}", OrderStatus.Cancelled, 1m)));
        context.Orders.AddRange(Enumerable.Range(0, 250).Select(index =>
            NewLegacyOrder(legacyOnly, $"REF-{index}", OrderStatus.Refunded, 1m)));
        var tenantDate = DateOnly.FromDateTime(
            _tenantClock.ToTenantTime(_serverTime.GetUtcNow().UtcDateTime).DateTime);
        context.Reservations.Add(NewReservation(stable.Id, tenantDate.AddDays(1)));
        context.Reservations.Add(NewReservation(futureReserved.Id, tenantDate.AddDays(1)));
        await context.SaveChangesAsync();

        _stableTableId = stable.Id;
        _futureReservedTableId = futureReserved.Id;
        _legacyOnlyTableId = legacyOnly.Id;
        _numericTableId = numeric.Id;
        _leadingZeroTableId = leadingZero.Id;
        _sessionId = stableSession.Id;
    }

    private static Table NewTable(string label, Guid floorPlanId) => new()
    {
        Id = Guid.NewGuid(),
        TableNumber = label,
        MaxGuests = 4,
        FloorPlanId = floorPlanId,
        CreatedBy = "test"
    };

    private static TableServiceSession NewSession(Guid? tableId, int? number) => new()
    {
        Id = Guid.NewGuid(),
        TableId = tableId,
        TableNumber = number,
        Currency = "CHF",
        Status = TableServiceSessionStatus.Open,
        Version = 1,
        OpenedAt = DateTime.UtcNow.AddHours(-1),
        CreatedBy = "test"
    };

    private static Order NewOrder(Table table, Guid sessionId) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = "SNAP-READY",
        Type = OrderType.DineIn,
        Status = OrderStatus.Ready,
        PaymentStatus = PaymentStatus.Pending,
        TableId = table.Id,
        TableLabel = table.TableNumber,
        ServiceSessionId = sessionId,
        Total = 25m,
        RemainingAmount = 25m,
        OrderDate = DateTime.UtcNow.AddMinutes(-30),
        CreatedBy = "test"
    };

    private static Order NewSessionOrder(
        Table table,
        Guid sessionId,
        string number,
        OrderStatus status,
        PaymentStatus paymentStatus,
        decimal total,
        decimal totalPaid,
        decimal remaining,
        OrderPayment? payment = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            Type = OrderType.DineIn,
            Status = status,
            PaymentStatus = paymentStatus,
            TableId = table.Id,
            TableLabel = table.TableNumber,
            ServiceSessionId = sessionId,
            Total = total,
            TotalPaid = totalPaid,
            RemainingAmount = remaining,
            OrderDate = DateTime.UtcNow.AddMinutes(-15),
            CreatedBy = "test"
        };
        if (payment is not null)
        {
            order.Payments.Add(payment);
        }

        return order;
    }

    private static Order NewLegacyOrder(
        Table table, string orderNumber, OrderStatus status, decimal total) => new()
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = OrderType.DineIn,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            TableId = table.Id,
            TableLabel = table.TableNumber,
            Total = total,
            RemainingAmount = total,
            OrderDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = "test"
        };

    private static string TestEmail(string localPart) =>
        string.Concat(localPart, "@", "example.test");

    private static Reservation NewReservation(Guid tableId, DateOnly localDate) => new()
    {
        Id = Guid.NewGuid(),
        TableId = tableId,
        CustomerName = "Snapshot guest",
        CustomerEmail = TestEmail("snapshot"),
        ReservationDate = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        StartTime = TimeSpan.FromHours(12),
        EndTime = TimeSpan.FromHours(13),
        NumberOfGuests = 2,
        Status = ReservationStatus.Confirmed,
        CreatedBy = "test"
    };

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class TablesReadGate : DbCommandInterceptor
    {
        private int _arrivals;
        public TaskCompletionSource<bool> TablesRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Tables\"", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _arrivals) == 1)
            {
                TablesRead.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TestCurrentUser(UserRole role) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "floor-test";
        public string? Email => TestEmail("floor-test");
        public UserRole? Role => role;
        public bool IsAuthenticated => true;
        public bool IsAdmin => role == UserRole.Admin;
        public Task<ApplicationUser?> GetUserAsync() => Task.FromResult<ApplicationUser?>(null);
        public string GetAuditIdentifier() => "floor-test";
    }
}
