using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Reads session metadata and its immutable-member bill as one contract.</summary>
public sealed class TableServiceSessionReader : ITableServiceSessionReader
{
    private readonly ApplicationDbContext _context;
    private readonly ITableBillAssembler _bills;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;
    private readonly ICurrentUserService? _currentUser;

    public TableServiceSessionReader(
        ApplicationDbContext context,
        ITableBillAssembler bills,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null,
        ICurrentUserService? currentUser = null)
    {
        _context = context;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _currentUser = currentUser;
    }

    public async Task<TableServiceSessionDto?> ReadAsync(
        Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var session = await _context.TableServiceSessions
            .AsNoTracking()
            .Include(value => value.Table)
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        // A session opened before the Currency column shipped (or while RestaurantInfo.Currency was
        // still null) carries Currency = null, which the cashier's Tables screen rendered as
        // "Currency unavailable". Fall back to the tenant's declared currency — the same fallback
        // OpenTableServiceSessionCommand uses when opening NEW sessions — so this only heals
        // historical rows; a session's own value still wins.
        var tenantCurrency = await ReadTenantCurrencyAsync(cancellationToken);
        var currency = CurrencyCode.Normalize(session.Currency) ?? tenantCurrency;
        var bill = await _bills.AssembleAsync(serviceSessionId, cancellationToken)
            ?? new TableBillDto
            {
                TableNumber = session.TableNumber,
                TableId = session.TableId,
                TableLabel = session.Table?.TableNumber,
                ServiceSessionId = session.Id,
                ServiceSessionVersion = session.Version,
                Currency = currency,
                GeneratedAt = now,
            };

        bill.ServiceSessionId = session.Id;
        bill.ServiceSessionVersion = session.Version;
        bill.Currency = currency;
        bill.GeneratedAt = now;
        var legacy = await ReadLegacyOrdersAsync(
            session.TableId, session.TableNumber, cancellationToken);
        var handoff = await TableServicePaymentHandoffReader.ReadLatestAsync(
            _context, session, cancellationToken);
        return ToDto(session, bill, legacy, handoff, now, currency);
    }

    public async Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // One row, resolved once per call so the session loop below never queries per session. See
        // ReadAsync for why the fallback is safe.
        var tenantCurrency = await ReadTenantCurrencyAsync(cancellationToken);
        var sessionRows = await _context.TableServiceSessions
            .AsNoTracking()
            .Include(value => value.Table)
            .Where(value => value.Status == TableServiceSessionStatus.Open)
            .OrderBy(value => value.TableNumber)
            .ToListAsync(cancellationToken);
        var bills = await _bills.AssembleManyAsync(sessionRows, cancellationToken);
        var legacyBySession = await ReadLegacyOrdersBySessionAsync(sessionRows, cancellationToken);
        var handoffs = await TableServicePaymentHandoffReader.ReadLatestManyAsync(
            _context, sessionRows, cancellationToken);
        var sessions = new List<TableServiceSessionDto>(sessionRows.Count);

        for (var index = 0; index < sessionRows.Count; index++)
        {
            var session = sessionRows[index];
            var currency = CurrencyCode.Normalize(session.Currency) ?? tenantCurrency;
            var bill = bills[index] ?? new TableBillDto
            {
                TableNumber = session.TableNumber,
                TableId = session.TableId,
                TableLabel = session.Table?.TableNumber,
                ServiceSessionId = session.Id,
                ServiceSessionVersion = session.Version,
                Currency = currency,
                GeneratedAt = now,
            };
            bill.ServiceSessionId = session.Id;
            bill.ServiceSessionVersion = session.Version;
            bill.Currency = currency;
            bill.GeneratedAt = now;
            legacyBySession.TryGetValue(session.Id, out var legacy);
            handoffs.TryGetValue(session.Id, out var handoff);
            sessions.Add(ToDto(session, bill, legacy ?? [], handoff, now, currency));
        }

        return sessions;
    }

    // The same precedence OpenTableServiceSessionCommand applies when opening a session: the
    // session's own normalized value first, then the tenant's declared currency.
    private async Task<string?> ReadTenantCurrencyAsync(CancellationToken cancellationToken) =>
        CurrencyCode.Normalize(await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken));

    private async Task<List<TableServiceSessionOrderState>> ReadLegacyOrdersAsync(
        Guid? tableId, int? tableNumber, CancellationToken cancellationToken)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null);
        query = TableServiceSessionCloseRules.ForUnassignedSession(
            query, tableId, tableNumber);
        var rows = await query
            .Select(order => new { order.Status, order.RemainingAmount })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new TableServiceSessionOrderState(row.Status, row.RemainingAmount)).ToList();
    }

    private async Task<Dictionary<Guid, List<TableServiceSessionOrderState>>> ReadLegacyOrdersBySessionAsync(
        IReadOnlyList<TableServiceSession> sessions, CancellationToken cancellationToken)
    {
        var tableIds = sessions
            .Where(session => session.TableId.HasValue)
            .Select(session => session.TableId!.Value)
            .Distinct()
            .ToArray();
        var tableNumbers = sessions
            .Where(session => session.TableNumber.HasValue)
            .Select(session => session.TableNumber!.Value)
            .Distinct()
            .ToArray();
        if (tableIds.Length == 0 && tableNumbers.Length == 0)
        {
            return [];
        }

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .Where(order => (order.TableId.HasValue && tableIds.Contains(order.TableId.Value))
                || (!order.TableId.HasValue && order.TableNumber.HasValue
                    && tableNumbers.Contains(order.TableNumber.Value)))
            .Select(order => new
            {
                order.TableId,
                order.TableNumber,
                order.Status,
                order.RemainingAmount
            })
            .ToListAsync(cancellationToken);
        var byTableId = rows
            .Where(row => row.TableId.HasValue)
            .GroupBy(row => row.TableId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var byTableNumber = rows
            .Where(row => !row.TableId.HasValue && row.TableNumber.HasValue)
            .GroupBy(row => row.TableNumber!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        return sessions.ToDictionary(session => session.Id, session =>
        {
            var stableRows = session.TableId.HasValue
                && byTableId.TryGetValue(session.TableId.Value, out var idRows)
                ? idRows
                : [];
            var legacyRows = session.TableNumber.HasValue
                && byTableNumber.TryGetValue(session.TableNumber.Value, out var numberRows)
                ? numberRows
                : [];
            return stableRows.Concat(legacyRows)
                .Select(row => new TableServiceSessionOrderState(row.Status, row.RemainingAmount))
                .ToList();
        });
    }

    private TableServiceSessionDto ToDto(
        TableServiceSession session,
        TableBillDto bill,
        IReadOnlyCollection<TableServiceSessionOrderState> legacy,
        TableServicePaymentHandoffDto? handoff,
        DateTime now,
        string? currency)
    {
        var members = bill.Rounds.Select(round =>
            new TableServiceSessionOrderState(ParseStatus(round.Order.Status), round.Order.RemainingAmount));
        var assessment = TableServiceSessionCloseRules.Assess(members, legacy, _paymentTolerance);
        var isOpen = session.Status == TableServiceSessionStatus.Open;
        var hasPendingHandoff = handoff?.Status == nameof(TableServicePaymentHandoffStatus.Requested);
        var hasTenderRole = _currentUser is null
            || _currentUser.IsAdmin
            || _currentUser.Role == UserRole.Cashier;
        return new TableServiceSessionDto
        {
            ServiceSessionId = session.Id,
            TableId = session.TableId,
            TableNumber = session.TableNumber,
            TableLabel = session.Table?.TableNumber
                ?? session.TableNumber?.ToString(CultureInfo.InvariantCulture)
                ?? string.Empty,
            Currency = currency,
            Status = session.Status.ToString(),
            Version = session.Version,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            RoundCount = bill.OrderCount,
            AgeMinutes = Math.Max(0, (int)(now - session.OpenedAt).TotalMinutes),
            Outstanding = bill.Remaining,
            EligibleOutstanding = bill.EligibleOutstanding,
            CanCollect = hasTenderRole && isOpen && bill.EligibleOutstanding > _paymentTolerance,
            CanRequestPaymentHandoff = !hasTenderRole && isOpen
                && bill.EligibleOutstanding > _paymentTolerance && !hasPendingHandoff,
            HasPendingPaymentHandoff = hasPendingHandoff,
            CanClose = isOpen && assessment.CanClose && !hasPendingHandoff,
            HasUnassignedActiveOrders = assessment.LegacyActiveOrderCount > 0,
            LegacyActiveOrderCount = assessment.LegacyActiveOrderCount,
            Bill = bill,
            PaymentHandoff = handoff,
        };
    }

    private static OrderStatus ParseStatus(string value) =>
        Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var status)
            ? status
            : OrderStatus.Pending;
}
