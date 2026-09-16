using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
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

    public TableServiceSessionReader(
        ApplicationDbContext context,
        ITableBillAssembler bills,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
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

        var currency = CurrencyCode.Normalize(session.Currency);
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
        return ToDto(session, bill, legacy, now);
    }

    public async Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var sessionRows = await _context.TableServiceSessions
            .AsNoTracking()
            .Include(value => value.Table)
            .Where(value => value.Status == TableServiceSessionStatus.Open)
            .OrderBy(value => value.TableNumber)
            .ToListAsync(cancellationToken);
        var bills = await _bills.AssembleManyAsync(sessionRows, cancellationToken);
        var legacyBySession = await ReadLegacyOrdersBySessionAsync(sessionRows, cancellationToken);
        var sessions = new List<TableServiceSessionDto>(sessionRows.Count);

        for (var index = 0; index < sessionRows.Count; index++)
        {
            var session = sessionRows[index];
            var currency = CurrencyCode.Normalize(session.Currency);
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
            sessions.Add(ToDto(session, bill, legacy ?? [], now));
        }

        return sessions;
    }

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
        DateTime now)
    {
        var members = bill.Rounds.Select(round =>
            new TableServiceSessionOrderState(ParseStatus(round.Order.Status), round.Order.RemainingAmount));
        var assessment = TableServiceSessionCloseRules.Assess(members, legacy, _paymentTolerance);
        var isOpen = session.Status == TableServiceSessionStatus.Open;
        return new TableServiceSessionDto
        {
            ServiceSessionId = session.Id,
            TableId = session.TableId,
            TableNumber = session.TableNumber,
            TableLabel = session.Table?.TableNumber
                ?? session.TableNumber?.ToString(CultureInfo.InvariantCulture)
                ?? string.Empty,
            Currency = CurrencyCode.Normalize(session.Currency),
            Status = session.Status.ToString(),
            Version = session.Version,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            RoundCount = bill.OrderCount,
            AgeMinutes = Math.Max(0, (int)(now - session.OpenedAt).TotalMinutes),
            Outstanding = bill.Remaining,
            EligibleOutstanding = bill.EligibleOutstanding,
            CanCollect = isOpen && bill.EligibleOutstanding > _paymentTolerance,
            CanClose = isOpen && assessment.CanClose,
            HasUnassignedActiveOrders = assessment.LegacyActiveOrderCount > 0,
            LegacyActiveOrderCount = assessment.LegacyActiveOrderCount,
            Bill = bill,
        };
    }

    private static OrderStatus ParseStatus(string value) =>
        Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var status)
            ? status
            : OrderStatus.Pending;
}
