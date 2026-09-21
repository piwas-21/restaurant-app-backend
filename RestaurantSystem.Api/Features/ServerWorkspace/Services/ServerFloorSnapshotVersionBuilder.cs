using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using FloorPlanEntity = RestaurantSystem.Domain.Entities.FloorPlan;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal static class ServerFloorSnapshotVersionBuilder
{
    public static string Create(
        IEnumerable<FloorPlanEntity> plans,
        IEnumerable<Table> tables,
        IEnumerable<FloorSessionRow> sessions,
        IEnumerable<FloorOrderRow> orders,
        IReadOnlyDictionary<Guid, ServerFloorReservationDto> reservations,
        DateTimeOffset? nextStateChangeAt,
        decimal paymentTolerance)
    {
        var source = string.Join('|', plans.OrderBy(plan => plan.Id)
                .Select(plan => $"p:{plan.Id}:{plan.UpdatedAt:O}:{plan.Name}:{plan.WidthMeters}:{plan.HeightMeters}:"
                    + $"{plan.GridSizeCm}:{plan.BackgroundStyle}:{plan.IsDefault}:{plan.DisplayOrder}:"
                    + string.Join(',', plan.Walls.OrderBy(wall => wall.Id).Select(wall =>
                        $"w:{wall.Id}:{wall.UpdatedAt:O}:{wall.PointsJson}:{wall.ThicknessMeters}:{wall.IsClosed}:"
                        + $"{wall.RoomName}:{wall.FloorStyle}:{wall.ZIndex}:"
                        + string.Join(',', wall.Openings.OrderBy(opening => opening.Id).Select(opening =>
                            $"x:{opening.Id}:{opening.UpdatedAt:O}:{opening.SegmentIndex}:{opening.OffsetMeters}:"
                            + $"{opening.WidthMeters}:{opening.Kind}:{opening.SwingDirection}"))))
                    + string.Join(',', plan.Items.OrderBy(item => item.Id).Select(item =>
                        $"i:{item.Id}:{item.UpdatedAt:O}:{item.Kind}:{item.X}:{item.Y}:{item.WidthMeters}:"
                        + $"{item.HeightMeters}:{item.RotationDegrees}:{item.ZIndex}:{item.Label}:{item.StyleVariant}"))))
            + ';' + string.Join('|', tables.OrderBy(table => table.Id)
                .Select(table => $"t:{table.Id}:{table.UpdatedAt:O}:{table.TableNumber}:{table.IsActive}:"
                    + $"{table.FloorPlanId}:{table.IsOutdoor}:{table.MaxGuests}:{table.PositionX}:{table.PositionY}:"
                    + $"{table.Width}:{table.Height}:{table.Shape}:{table.Rotation}"))
            + ';' + string.Join('|', sessions.OrderBy(session => session.Id)
                .Select(session => $"s:{session.Id}:{session.Version}:{session.TableId}:{session.TableNumber}:"
                    + $"{session.Currency}"))
            + ';' + string.Join('|', orders.Where(order => IsVersionRelevant(order, paymentTolerance))
                .OrderBy(order => order.Id)
                .Select(order => $"o:{order.Id}:{order.ServiceSessionId}:{order.TableId}:{order.TableNumber}:"
                    + $"{order.Status}:{order.Total}:{order.TotalPaid}:{order.RemainingAmount}:{order.CanCollect}"))
            + ';' + string.Join('|', reservations.OrderBy(item => item.Key)
                .Select(item => $"r:{item.Value.ReservationId}:{item.Key}:{item.Value.CustomerName}:"
                    + $"{item.Value.ReservationDate:O}:{item.Value.Status}:{item.Value.StartTime}:"
                    + $"{item.Value.EndTime}:{item.Value.GuestCount}:{item.Value.IsCurrent}"))
            + $";next:{nextStateChangeAt:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    }

    private static bool IsVersionRelevant(FloorOrderRow order, decimal paymentTolerance) =>
        order.ServiceSessionId.HasValue
            ? order.Status != OrderStatus.Cancelled
            : IsOccupyingLegacy(order) || TableServiceSessionCloseRules.IsBlockingLegacyOrder(
                new TableServiceSessionOrderState(order.Status, order.RemainingAmount), paymentTolerance);

    private static bool IsOccupyingLegacy(FloorOrderRow order) =>
        order.Status is OrderStatus.Pending or OrderStatus.PendingApproval or OrderStatus.Confirmed
            or OrderStatus.Preparing or OrderStatus.Ready
        || order.Status == OrderStatus.Completed && order.CanCollect;
}
