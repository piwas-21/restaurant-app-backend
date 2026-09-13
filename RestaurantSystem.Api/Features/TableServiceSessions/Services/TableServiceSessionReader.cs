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
                ServiceSessionId = session.Id,
                ServiceSessionVersion = session.Version,
                Currency = currency,
                GeneratedAt = now,
            };

        bill.ServiceSessionId = session.Id;
        bill.ServiceSessionVersion = session.Version;
        bill.Currency = currency;
        bill.GeneratedAt = now;
        var legacy = await ReadLegacyOrdersAsync(session.TableNumber, cancellationToken);
        return ToDto(session, bill, legacy, now);
    }

    public async Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var sessionRows = await _context.TableServiceSessions
            .AsNoTracking()
            .Where(value => value.Status == TableServiceSessionStatus.Open)
            .OrderBy(value => value.TableNumber)
            .ToListAsync(cancellationToken);
        var bills = await _bills.AssembleManyAsync(sessionRows, cancellationToken);
        var legacyByTable = await ReadLegacyOrdersByTableAsync(
            sessionRows.Select(session => session.TableNumber).Distinct().ToArray(), cancellationToken);
        var sessions = new List<TableServiceSessionDto>(sessionRows.Count);

        for (var index = 0; index < sessionRows.Count; index++)
        {
            var session = sessionRows[index];
            var currency = CurrencyCode.Normalize(session.Currency);
            var bill = bills[index] ?? new TableBillDto
            {
                TableNumber = session.TableNumber,
                ServiceSessionId = session.Id,
                ServiceSessionVersion = session.Version,
                Currency = currency,
                GeneratedAt = now,
            };
            bill.ServiceSessionId = session.Id;
            bill.ServiceSessionVersion = session.Version;
            bill.Currency = currency;
            bill.GeneratedAt = now;
            legacyByTable.TryGetValue(session.TableNumber, out var legacy);
            sessions.Add(ToDto(session, bill, legacy ?? [], now));
        }

        return sessions;
    }

    private async Task<List<TableServiceSessionOrderState>> ReadLegacyOrdersAsync(
        int tableNumber, CancellationToken cancellationToken)
    {
        var rows = await _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.TableNumber == tableNumber
                && order.ServiceSessionId == null)
            .Select(order => new { order.Status, order.RemainingAmount })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new TableServiceSessionOrderState(row.Status, row.RemainingAmount)).ToList();
    }

    private async Task<Dictionary<int, List<TableServiceSessionOrderState>>> ReadLegacyOrdersByTableAsync(
        int[] tableNumbers, CancellationToken cancellationToken)
    {
        if (tableNumbers.Length == 0)
        {
            return [];
        }

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.TableNumber.HasValue
                && tableNumbers.Contains(order.TableNumber.Value)
                && order.ServiceSessionId == null)
            .Select(order => new { TableNumber = order.TableNumber!.Value, order.Status, order.RemainingAmount })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.TableNumber)
            .ToDictionary(group => group.Key, group => group
                .Select(row => new TableServiceSessionOrderState(row.Status, row.RemainingAmount))
                .ToList());
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
            TableNumber = session.TableNumber,
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
