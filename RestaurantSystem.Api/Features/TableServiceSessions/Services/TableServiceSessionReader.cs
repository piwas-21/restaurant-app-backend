using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Reads session metadata and its immutable-member bill as one contract.</summary>
public sealed partial class TableServiceSessionReader : ITableServiceSessionReader
{
    private readonly ApplicationDbContext _context;
    private readonly ITableBillAssembler _bills;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;
    private readonly int _accountActivityPageSize;
    private readonly ICurrentUserService? _currentUser;
    private readonly IAccountPaymentActorResolver? _paymentActors;

    public TableServiceSessionReader(
        ApplicationDbContext context,
        ITableBillAssembler bills,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null,
        ICurrentUserService? currentUser = null,
        IAccountPaymentActorResolver? paymentActors = null)
    {
        _context = context;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _accountActivityPageSize = (settings?.Value ?? new TableServiceSessionSettings()).AccountActivityPageSize;
        _currentUser = currentUser;
        _paymentActors = paymentActors;
    }

    public async Task<TableServiceSessionDto?> ReadAsync(
        Guid serviceSessionId, CancellationToken cancellationToken)
    {
        await using var snapshot = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken)
            : null;

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
                AccountRevision = session.AccountRevision,
                Currency = currency,
                GeneratedAt = now,
            };

        bill.ServiceSessionId = session.Id;
        bill.ServiceSessionVersion = session.Version;
        bill.AccountRevision = session.AccountRevision;
        bill.Currency = currency;
        bill.GeneratedAt = now;
        var activity = await TableAccountActivityReader.ReadManyAsync(_context, [session.Id], _accountActivityPageSize, cancellationToken);
        ApplyActivity(bill, session.Id, activity);
        var legacy = await ReadLegacyOrdersAsync(
            session.TableId, session.TableNumber, cancellationToken);
        var handoff = await TableServicePaymentHandoffReader.ReadLatestAsync(
            _context, session, cancellationToken);
        return ToDto(session, bill, legacy, handoff, now, currency);
    }

    public Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(CancellationToken cancellationToken) =>
        ReadSessionsAsync(released: false, cancellationToken);

    public Task<IReadOnlyList<TableServiceSessionDto>> ReadReleasedAsync(CancellationToken cancellationToken) =>
        ReadSessionsAsync(released: true, cancellationToken);

    private async Task<IReadOnlyList<TableServiceSessionDto>> ReadSessionsAsync(
        bool released, CancellationToken cancellationToken)
    {
        await using var snapshot = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken)
            : null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // One row, resolved once per call so the session loop below never queries per session. See
        // ReadAsync for why the fallback is safe.
        var tenantCurrency = await ReadTenantCurrencyAsync(cancellationToken);
        var sessionRows = await _context.TableServiceSessions
            .AsNoTracking()
            .Include(value => value.Table)
            .Where(value => value.Status == TableServiceSessionStatus.Open
                && (released ? value.ReleasedAt != null : value.ReleasedAt == null))
            .OrderBy(value => value.TableNumber)
            .ToListAsync(cancellationToken);
        var bills = await _bills.AssembleManyAsync(sessionRows, cancellationToken);
        var legacyBySession = await ReadLegacyOrdersBySessionAsync(sessionRows, cancellationToken);
        var handoffs = await TableServicePaymentHandoffReader.ReadLatestManyAsync(
            _context, sessionRows, cancellationToken);
        var activity = await TableAccountActivityReader.ReadManyAsync(
            _context, sessionRows.Select(session => session.Id).ToArray(), _accountActivityPageSize, cancellationToken);
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
                AccountRevision = session.AccountRevision,
                Currency = currency,
                GeneratedAt = now,
            };
            bill.ServiceSessionId = session.Id;
            bill.ServiceSessionVersion = session.Version;
            bill.AccountRevision = session.AccountRevision;
            bill.Currency = currency;
            bill.GeneratedAt = now;
            ApplyActivity(bill, session.Id, activity);
            legacyBySession.TryGetValue(session.Id, out var legacy);
            handoffs.TryGetValue(session.Id, out var handoff);
            sessions.Add(ToDto(session, bill, legacy ?? [], handoff, now, currency));
        }

        return sessions;
    }

    private static void ApplyActivity(TableBillDto bill, Guid sessionId,
        Dictionary<Guid, TableAccountActivityPage> activity)
    {
        if (!activity.TryGetValue(sessionId, out var page)) return;
        bill.AccountActivity = page.Events;
        bill.HasMoreAccountActivity = page.HasMore;
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
            .SelectCloseCharges()
            .ToListAsync(cancellationToken);
        return rows.Select(row => row.ToState()).ToList();
    }

    private TableServiceSessionDto ToDto(
        TableServiceSession session,
        TableBillDto bill,
        IReadOnlyCollection<TableServiceSessionOrderState> legacy,
        TableServicePaymentHandoffDto? handoff,
        DateTime now,
        string? currency)
    {
        var memberStates = bill.Rounds.Select(round =>
            new TableServiceSessionOrderState(ParseStatus(round.Order.Status), round.Outstanding,
                round.Order.PaymentStatus == nameof(PaymentStatus.Refunded))).ToList();
        bill.Remaining = memberStates.Sum(TableServiceSessionCloseRules.Outstanding);
        var assessment = TableServiceSessionCloseRules.Assess(memberStates, legacy, _paymentTolerance);
        var isOpen = session.Status == TableServiceSessionStatus.Open;
        var isTableReleased = session.ReleasedAt.HasValue;
        var hasPendingHandoff = handoff?.Status == nameof(TableServicePaymentHandoffStatus.Requested);
        var hasTenderRole = _currentUser is null
            || _currentUser.IsAdmin
            || _currentUser.Role == UserRole.Cashier
            || _currentUser.Role == UserRole.Server && _paymentActors?.CanStartCollection == true;
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
            AccountRevision = session.AccountRevision,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            ReleasedAt = session.ReleasedAt,
            ReleasedBy = session.ReleasedBy,
            IsTableReleased = isTableReleased,
            RoundCount = bill.OrderCount,
            AgeMinutes = Math.Max(0, (int)(now - session.OpenedAt).TotalMinutes),
            Outstanding = assessment.Outstanding,
            EligibleOutstanding = bill.EligibleOutstanding,
            CanCollect = hasTenderRole && isOpen && bill.EligibleOutstanding > _paymentTolerance,
            CanRequestPaymentHandoff = !hasTenderRole && isOpen
                && bill.EligibleOutstanding > _paymentTolerance && !hasPendingHandoff,
            HasPendingPaymentHandoff = hasPendingHandoff,
            CanClose = isOpen && assessment.CanClose && !hasPendingHandoff,
            CanReleaseTable = isOpen && !isTableReleased && !hasPendingHandoff
                && assessment.LegacyActiveOrderCount == 0,
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
