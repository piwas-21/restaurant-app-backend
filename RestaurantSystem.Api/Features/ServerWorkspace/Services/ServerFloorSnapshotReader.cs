using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FloorPlan.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using FloorPlanEntity = RestaurantSystem.Domain.Entities.FloorPlan;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

/// <summary>
/// Reads the floor as one repeatable-read snapshot. When the request already owns a transaction,
/// that transaction's isolation level is authoritative; the caller must provide repeatable-read
/// semantics if it requires the same guarantee. A new transaction is created at repeatable-read
/// when no ambient transaction exists.
/// </summary>
public sealed class ServerFloorSnapshotReader : IServerFloorSnapshotReader
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableBillAssembler _bills;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;
    private readonly int _reservationLookAheadDays;

    public ServerFloorSnapshotReader(
        ApplicationDbContext context,
        ITenantClock clock,
        ICurrentUserService currentUser,
        ITableBillAssembler bills,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _clock = clock;
        _currentUser = currentUser;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var sessionSettings = settings?.Value ?? new TableServiceSessionSettings();
        _paymentTolerance = sessionSettings.PaymentTolerance;
        _reservationLookAheadDays = sessionSettings.FloorReservationLookAheadDays;
    }

    public async Task<ServerFloorSnapshotDto> ReadAsync(CancellationToken cancellationToken)
    {
        await using var transaction = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken)
            : null;

        var serverTime = _timeProvider.GetUtcNow().UtcDateTime;
        var tenantTime = _clock.ToTenantTime(serverTime);
        var tenantCurrency = await ReadTenantCurrencyAsync(cancellationToken);
        var plans = await LoadPlansAsync(cancellationToken);
        var tables = await LoadTablesAsync(cancellationToken);
        var sessionEntities = await LoadSessionsAsync(cancellationToken);
        var sessions = await BuildSessionRowsAsync(sessionEntities, tenantCurrency, cancellationToken);
        var orders = await LoadOrdersAsync(tables, sessions, cancellationToken);
        var reservations = await LoadReservationsAsync(tenantTime, cancellationToken);
        var projection = new ServerFloorSnapshotProjector(
            _clock, _currentUser, _paymentTolerance).Project(
                plans, tables, sessions, orders, reservations, tenantTime, serverTime);
        var snapshot = new ServerFloorSnapshotDto
        {
            ServerTime = serverTime,
            TenantTime = tenantTime,
            NextStateChangeAt = projection.NextStateChangeAt,
            Version = projection.Version,
            Cursor = projection.Version,
            Zones = plans.Select(plan => FloorPlanDocumentMapper.ToDocumentDto(
                plan, tables.Where(table => table.FloorPlanId == plan.Id).ToList())).ToList(),
            Tables = projection.Tables
        };

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return snapshot;
    }

    private async Task<string?> ReadTenantCurrencyAsync(CancellationToken cancellationToken) =>
        CurrencyCode.Normalize(await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken));

    private async Task<List<FloorPlanEntity>> LoadPlansAsync(CancellationToken cancellationToken) =>
        await _context.FloorPlans.AsNoTracking()
            .Include(plan => plan.Walls).ThenInclude(wall => wall.Openings)
            .Include(plan => plan.Items)
            .AsSplitQuery()
            .OrderByDescending(plan => plan.IsDefault)
            .ThenBy(plan => plan.DisplayOrder)
            .ToListAsync(cancellationToken);

    private Task<List<Table>> LoadTablesAsync(CancellationToken cancellationToken) =>
        _context.Tables.AsNoTracking()
            .OrderBy(table => table.TableNumber)
            .ToListAsync(cancellationToken);

    private Task<List<TableServiceSession>> LoadSessionsAsync(CancellationToken cancellationToken) =>
        _context.TableServiceSessions.AsNoTracking()
            .Where(session => session.Status == TableServiceSessionStatus.Open)
            .OrderBy(session => session.TableNumber)
            .ToListAsync(cancellationToken);

    private async Task<List<FloorSessionRow>> BuildSessionRowsAsync(
        List<TableServiceSession> sessionEntities,
        string? tenantCurrency,
        CancellationToken cancellationToken)
    {
        var bills = await _bills.AssembleManyAsync(sessionEntities, cancellationToken);
        var rows = new List<FloorSessionRow>(sessionEntities.Count);
        for (var index = 0; index < sessionEntities.Count; index++)
        {
            var session = sessionEntities[index];
            rows.Add(new FloorSessionRow(
                session.Id,
                session.TableId,
                session.TableNumber,
                CurrencyCode.Normalize(session.Currency) ?? tenantCurrency,
                session.Version,
                session.OpenedAt,
                bills[index]));
        }

        return rows;
    }

    private async Task<List<FloorOrderRow>> LoadOrdersAsync(
        IReadOnlyCollection<Table> tables,
        IReadOnlyCollection<FloorSessionRow> sessions,
        CancellationToken cancellationToken)
    {
        var tableIds = tables.Select(table => table.Id).ToArray();
        var tableNumbers = tables.Select(table =>
                ServerFloorSnapshotProjector.TryCanonicalNumber(table.TableNumber, out var number)
                    ? (int?)number : null)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToArray();
        var legacy = _context.Orders.AsNoTracking()
            .Where(order => !order.IsDeleted && order.Type == OrderType.DineIn)
            .Where(order => !order.ServiceSessionId.HasValue
                && ((order.TableId.HasValue && tableIds.Contains(order.TableId.Value))
                    || (!order.TableId.HasValue && order.TableNumber.HasValue
                        && tableNumbers.Contains(order.TableNumber.Value))))
            .Where(OrderSettlementEligibility.OperationalQueuePredicate());
        // The explicit-session bill above is the authoritative settlement projection. This query
        // is intentionally limited to unassigned operational rows: terminal history cannot make a
        // table occupied or ambiguous and must not scale the floor read with old receipts.
        var collectibleLegacyIds = await legacy.Where(OrderSettlementEligibility.CanCollectQuery())
            .Select(order => order.Id)
            .ToHashSetAsync(cancellationToken);
        var legacyRows = await legacy
            .Select(order => new FloorOrderRow(
                order.Id, null, order.TableId, order.TableNumber, order.Status,
                order.Total, order.TotalPaid, order.RemainingAmount, collectibleLegacyIds.Contains(order.Id)))
            .ToListAsync(cancellationToken);
        var sessionRows = sessions.SelectMany(session => (session.Bill?.Rounds ?? [])
            .Select(round => new FloorOrderRow(
                round.Order.Id,
                round.Order.ServiceSessionId ?? session.Id,
                round.Order.TableId,
                round.Order.TableNumber,
                ParseStatus(round.Order.Status),
                round.Order.Total,
                round.Order.TotalPaid,
                round.Order.RemainingAmount,
                round.CanCollect))).ToList();
        return sessionRows.Concat(legacyRows).ToList();
    }

    private static OrderStatus ParseStatus(string value) =>
        Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var status)
            ? status
            : OrderStatus.Pending;

    private async Task<List<ReservationRow>> LoadReservationsAsync(
        DateTimeOffset tenantTime, CancellationToken cancellationToken)
    {
        var start = DateOnly.FromDateTime(tenantTime.DateTime)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(_reservationLookAheadDays);
        return await _context.Reservations.AsNoTracking()
            .Where(reservation => reservation.ReservationDate >= start
                && reservation.ReservationDate < end
                && (reservation.Status == ReservationStatus.Pending
                    || reservation.Status == ReservationStatus.Confirmed))
            .Select(reservation => new ReservationRow(
                reservation.Id, reservation.CustomerName, reservation.ReservationDate,
                reservation.StartTime, reservation.EndTime, reservation.NumberOfGuests,
                reservation.Status, reservation.TableId,
                reservation.CombinedTables.Select(table => table.TableId).ToList()))
            .ToListAsync(cancellationToken);
    }
}
