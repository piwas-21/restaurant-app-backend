using System.Globalization;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FloorPlan.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using FloorPlanEntity = RestaurantSystem.Domain.Entities.FloorPlan;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal sealed class ServerFloorSnapshotProjector
{
    private static readonly OrderStatus[] ActiveRoundStatuses =
    [
        OrderStatus.Pending, OrderStatus.PendingApproval, OrderStatus.Confirmed,
        OrderStatus.Preparing, OrderStatus.Ready
    ];

    private readonly ITenantClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly decimal _paymentTolerance;

    public ServerFloorSnapshotProjector(
        ITenantClock clock, ICurrentUserService currentUser, decimal paymentTolerance)
    {
        _clock = clock;
        _currentUser = currentUser;
        _paymentTolerance = paymentTolerance;
    }

    public ServerFloorProjection Project(
        IReadOnlyCollection<FloorPlanEntity> plans,
        IReadOnlyCollection<Table> tables,
        IReadOnlyCollection<FloorSessionRow> sessions,
        IReadOnlyCollection<FloorOrderRow> orders,
        IReadOnlyCollection<ReservationRow> reservations,
        DateTimeOffset tenantTime,
        DateTime serverTime)
    {
        var reservationsByTable = ExpandReservations(reservations, tenantTime);
        var nextStateChangeAt = FindNextStateChangeAt(reservations, tenantTime);
        var sessionByTableId = sessions
            .Where(session => session.TableId.HasValue)
            .ToDictionary(session => session.TableId!.Value);
        var legacySessionByNumber = sessions
            .Where(session => !session.TableId.HasValue && session.TableNumber.HasValue)
            .ToDictionary(session => session.TableNumber!.Value);
        var zoneNames = plans.ToDictionary(plan => plan.Id, plan => plan.Name);
        var tableDtos = tables.Select(table => MapTable(
            table,
            ResolveSession(table, sessionByTableId, legacySessionByNumber),
            orders,
            reservationsByTable.GetValueOrDefault(table.Id),
            table.FloorPlanId is { } planId ? zoneNames.GetValueOrDefault(planId) : null,
            serverTime)).ToList();
        return new ServerFloorProjection(
            tableDtos,
            ServerFloorSnapshotVersionBuilder.Create(
                plans, tables, sessions, orders, reservationsByTable,
                nextStateChangeAt, _paymentTolerance),
            nextStateChangeAt);
    }

    private ServerFloorTableDto MapTable(
        Table table,
        FloorSessionRow? session,
        IReadOnlyCollection<FloorOrderRow> orders,
        ServerFloorReservationDto? reservation,
        string? zoneName,
        DateTime serverTime)
    {
        var legacy = LegacyForTable(table, orders);
        var legacyOperational = legacy.Where(IsOccupyingLegacy).ToList();
        var hasLegacyAmbiguity = legacy.Any(IsBlockingLegacy);
        var summary = session is null ? null : SummarizeSession(session, legacy, serverTime);
        var readyCount = summary?.ReadyRoundCount ?? legacyOperational.Count(IsReady);
        var state = DetermineTableState(
            table.IsActive, readyCount, summary is not null,
            hasLegacyAmbiguity, reservation?.IsCurrent == true);
        var legacyDto = legacyOperational.Count == 0 ? null : new ServerFloorLegacySummaryDto
        {
            OrderCount = legacyOperational.Count,
            ActiveOrderCount = legacyOperational.Count(IsActiveRound),
            ReadyOrderCount = legacyOperational.Count(IsReady),
            Outstanding = legacyOperational.Sum(order => Math.Max(0m, order.RemainingAmount))
        };

        return new ServerFloorTableDto
        {
            TableId = table.Id,
            TableLabel = table.TableNumber,
            ZoneId = table.FloorPlanId,
            ZoneName = zoneName,
            IsActive = table.IsActive,
            IsOutdoor = table.IsOutdoor,
            MaxGuests = table.MaxGuests,
            PositionX = table.PositionX,
            PositionY = table.PositionY,
            Width = table.Width,
            Height = table.Height,
            Shape = table.Shape,
            Rotation = table.Rotation,
            State = state,
            ActiveRoundCount = summary?.ActiveRoundCount ?? legacyDto?.ActiveOrderCount ?? 0,
            ReadyRoundCount = readyCount,
            Session = summary,
            Legacy = legacyDto,
            HasLegacyAmbiguity = hasLegacyAmbiguity,
            Reservation = reservation,
            PermittedActions = PermittedActions(
                table, summary, hasLegacyAmbiguity, legacyDto is not null, readyCount,
                reservation?.IsCurrent == true)
        };
    }
    private ServerFloorSessionSummaryDto SummarizeSession(
        FloorSessionRow session,
        IReadOnlyCollection<FloorOrderRow> legacy,
        DateTime serverTime)
    {
        var members = session.Bill?.Rounds
            .Select(round => new TableServiceSessionOrderState(
                ParseStatus(round.Order.Status), round.Outstanding))
            .ToList() ?? [];
        var legacyStates = legacy.Select(order =>
            new TableServiceSessionOrderState(order.Status, order.RemainingAmount));
        var assessment = TableServiceSessionCloseRules.Assess(
            members, legacyStates, _paymentTolerance);
        var bill = session.Bill;
        return new ServerFloorSessionSummaryDto
        {
            ServiceSessionId = session.Id,
            Version = session.Version,
            OpenedAt = session.OpenedAt,
            AgeMinutes = Math.Max(0, (int)(serverTime - session.OpenedAt).TotalMinutes),
            Currency = session.Currency,
            Total = bill?.Total ?? 0m,
            Paid = bill?.TotalPaid ?? 0m,
            Remaining = assessment.Outstanding,
            ActiveRoundCount = bill?.Rounds.Count(round => IsActiveRound(ParseStatus(round.Order.Status))) ?? 0,
            ReadyRoundCount = bill?.Rounds.Count(round => IsReady(ParseStatus(round.Order.Status))) ?? 0,
            CanCollect = bill?.EligibleOutstanding > _paymentTolerance,
            CanClose = assessment.CanClose,
            HasLegacyAmbiguity = assessment.LegacyActiveOrderCount > 0
        };
    }
    private static string DetermineTableState(
        bool isActive,
        int readyCount,
        bool hasOpenSession,
        bool hasLegacyAmbiguity,
        bool hasCurrentReservation)
    {
        if (!isActive) return "Inactive";
        if (readyCount > 0) return "Ready";
        if (hasOpenSession) return "Open";
        if (hasLegacyAmbiguity) return "Ambiguous";
        return hasCurrentReservation ? "Reserved" : "Available";
    }

    private List<string> PermittedActions(
        Table table,
        ServerFloorSessionSummaryDto? session,
        bool hasLegacyAmbiguity,
        bool hasLegacyOrders,
        int readyCount,
        bool hasCurrentReservation)
    {
        if (!table.IsActive)
        {
            return [];
        }

        if (session is null)
        {
            if (hasLegacyOrders || hasLegacyAmbiguity) return ["ReviewLegacy"];
            return hasCurrentReservation ? [] : ["StartTable"];
        }

        var actions = new List<string> { "AddRound", "ViewBill" };
        if (readyCount > 0)
        {
            actions.Add("OpenTasks");
        }

        if (session.CanCollect && _currentUser.Role is UserRole.Admin or UserRole.Cashier)
        {
            actions.Add("CollectPayment");
        }

        if (session.CanClose)
        {
            actions.Add("CloseVisit");
        }

        if (hasLegacyAmbiguity)
        {
            actions.Add("ReviewLegacy");
        }

        return actions;
    }

    private static FloorSessionRow? ResolveSession(
        Table table,
        Dictionary<Guid, FloorSessionRow> byTableId,
        Dictionary<int, FloorSessionRow> legacyByNumber)
    {
        if (byTableId.TryGetValue(table.Id, out var stable))
        {
            return stable;
        }

        return TryCanonicalNumber(table.TableNumber, out var number)
            && legacyByNumber.TryGetValue(number, out var legacy) ? legacy : null;
    }

    private static List<FloorOrderRow> LegacyForTable(Table table, IEnumerable<FloorOrderRow> orders) =>
        orders.Where(order => order.ServiceSessionId is null
            && ((order.TableId == table.Id)
                || (!order.TableId.HasValue
                    && TryCanonicalNumber(table.TableNumber, out var number)
                    && order.TableNumber == number)))
            .ToList();

    private static bool IsOccupyingLegacy(FloorOrderRow order) =>
        IsActiveRound(order)
        || order.Status == OrderStatus.Completed && order.CanCollect;

    private bool IsBlockingLegacy(FloorOrderRow order) =>
        TableServiceSessionCloseRules.IsBlockingLegacyOrder(
            new TableServiceSessionOrderState(order.Status, order.RemainingAmount), _paymentTolerance);

    private static OrderStatus ParseStatus(string value) =>
        Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var status)
            ? status
            : OrderStatus.Pending;

    private static bool IsActiveRound(FloorOrderRow order) => ActiveRoundStatuses.Contains(order.Status);

    private static bool IsActiveRound(OrderStatus status) => ActiveRoundStatuses.Contains(status);

    private static bool IsReady(FloorOrderRow order) => order.Status == OrderStatus.Ready;

    private static bool IsReady(OrderStatus status) => status == OrderStatus.Ready;

    private static Dictionary<Guid, ServerFloorReservationDto> ExpandReservations(
        IEnumerable<ReservationRow> reservations, DateTimeOffset tenantTime)
    {
        var candidates = reservations.SelectMany(reservation => reservation.AllTableIds.Select(tableId => new
        {
            TableId = tableId,
            Reservation = reservation,
            StartsAt = reservation.LocalStart,
            EndsAt = reservation.LocalEnd
        }))
        .Where(item => item.EndsAt > tenantTime.DateTime)
        .OrderByDescending(item => item.StartsAt <= tenantTime.DateTime)
        .ThenBy(item => item.StartsAt);
        return candidates.GroupBy(item => item.TableId).ToDictionary(
            group => group.Key,
            group => group.Select(item => new ServerFloorReservationDto
            {
                ReservationId = item.Reservation.Id,
                CustomerName = item.Reservation.CustomerName,
                ReservationDate = item.Reservation.Date,
                StartTime = item.Reservation.Start,
                EndTime = item.Reservation.End,
                GuestCount = item.Reservation.GuestCount,
                Status = item.Reservation.Status.ToString(),
                IsCurrent = item.StartsAt <= tenantTime.DateTime && item.EndsAt > tenantTime.DateTime
            }).First());
    }

    private DateTimeOffset? FindNextStateChangeAt(
        IEnumerable<ReservationRow> reservations, DateTimeOffset tenantTime)
    {
        var localNow = tenantTime.DateTime;
        var futureBoundaries = reservations.SelectMany(reservation => new[] { reservation.LocalStart, reservation.LocalEnd })
            .Where(boundary => boundary > localNow)
            .Select(ToTenantInstant)
            .Where(boundary => boundary > tenantTime)
            .OrderBy(boundary => boundary)
            .ToList();
        return futureBoundaries.Count == 0 ? null : futureBoundaries[0];
    }

    private DateTimeOffset ToTenantInstant(DateTime localTime)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, _clock.TimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    internal static bool TryCanonicalNumber(string label, out int number) =>
        int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out number)
        && number > 0
        && number.ToString(CultureInfo.InvariantCulture) == label;
}
