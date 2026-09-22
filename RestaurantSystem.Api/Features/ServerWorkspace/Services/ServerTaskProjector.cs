using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal sealed class ServerTaskProjector : IServerTaskProjector
{
    private readonly IOrderPermittedActionsService _actions;

    public ServerTaskProjector(IOrderPermittedActionsService actions) => _actions = actions;

    public ServerServiceTaskDto Project(Order order, DateTime serverTime)
    {
        var routing = order.RoutingStates
            .OrderBy(state => state.Target)
            .Select(ToRoutingDto)
            .ToList();
        var requiredException = ServerTaskRoutingPolicy.HasRequiredException(order);
        var optionalException = order.RoutingStates.Any(state =>
            ServerTaskRoutingPolicy.IsException(state) && !state.IsRequired);
        var ready = order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery;
        var bucket = requiredException
            ? "Exception"
            : ready ? "Ready" : "Overdue";
        var actionableAt = ResolveActionableAt(order, bucket);
        var handOver = _actions.GetPermittedActions(order)
            .Single(action => action.Action == nameof(OrderAction.HandOver));
        var table = order.Type == OrderType.DineIn;

        var deliveryAllowed = !requiredException && order.IsKitchenReleased && handOver.Allowed;
        var deliveryReason = deliveryAllowed
            ? null
            : requiredException
                ? ErrorCodes.RequiredRoutingUnresolved
            : !order.IsKitchenReleased
                ? ErrorCodes.KitchenReleaseRequired
                : handOver.ReasonCode;

        return new ServerServiceTaskDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OrderType = order.Type,
            Status = order.Status,
            Bucket = bucket,
            ActionableAt = actionableAt,
            AgeSeconds = Math.Max(0, (long)(serverTime - actionableAt).TotalSeconds),
            TableId = table ? order.TableId : null,
            TableLabel = table ? order.TableLabel : null,
            TableNumber = table ? order.TableNumber : null,
            ServiceSessionId = table ? order.ServiceSessionId : null,
            Total = order.Total,
            RemainingAmount = order.RemainingAmount,
            Version = order.Version,
            RoutingState = ResolveRoutingState(order.RoutingStates, requiredException, optionalException),
            HasRequiredRoutingException = requiredException,
            HasOptionalRoutingException = optionalException,
            Routing = routing,
            PermittedDeliveryActions =
            [new ServerTaskActionDto(nameof(OrderAction.HandOver), deliveryAllowed,
                deliveryReason, ResolveTargetStatus(order))],
        };
    }

    public List<ServerServiceTaskDto> FilterAndSort(
        IEnumerable<ServerServiceTaskDto> tasks,
        string bucket) =>
        tasks.Where(task => bucket.Length == 0
                || string.Equals(task.Bucket, bucket, StringComparison.OrdinalIgnoreCase))
            .OrderBy(task => BucketRank(task.Bucket))
            .ThenBy(task => task.ActionableAt)
            .ThenBy(task => task.OrderId)
            .ToList();

    public bool IsAfter(
        ServerServiceTaskDto task,
        int bucketRank,
        DateTime position,
        Guid positionId) =>
        BucketRank(task.Bucket) > bucketRank
        || BucketRank(task.Bucket) == bucketRank
            && (task.ActionableAt > position
                || task.ActionableAt == position && task.OrderId.CompareTo(positionId) > 0);

    public static int BucketRank(string bucket) => bucket switch
    {
        "Ready" => 0,
        "Overdue" => 1,
        "Exception" => 2,
        _ => 3,
    };

    private static DateTime ResolveActionableAt(Order order, string bucket)
    {
        if (bucket == "Exception")
        {
            var routeTime = order.RoutingStates
                .Where(ServerTaskRoutingPolicy.IsException)
                .Select(state => state.UpdatedAt ?? state.CreatedAt)
                .Where(value => value != default)
                .OrderBy(value => value)
                .FirstOrDefault();
            if (routeTime != default)
            {
                return Utc(routeTime);
            }
        }

        if (order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery)
        {
            var statusTime = order.StatusHistory
                .Where(history => history.ToStatus == order.Status)
                .Select(history => history.ChangedAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            return statusTime == default ? Utc(order.OrderDate) : Utc(statusTime);
        }

        return Utc(order.EstimatedDeliveryTime ?? order.OrderDate);
    }

    private static string ResolveRoutingState(
        ICollection<OrderRoutingState> states,
        bool requiredException,
        bool optionalException)
    {
        if (states.Count == 0) return "Unknown";
        if (requiredException) return "ExceptionRequired";
        if (optionalException) return "ExceptionOptional";
        if (states.Any(state => state.Status == DevicePrintStatus.Queued)) return "Queued";
        if (states.Any(state => state.Status is DevicePrintStatus.Received or DevicePrintStatus.Sent))
            return "Sent";
        if (states.All(state => state.Status is DevicePrintStatus.Printed or DevicePrintStatus.Skipped))
            return "Complete";
        return "Unknown";
    }

    private static OrderStatus? ResolveTargetStatus(Order order) =>
        order.Status == OrderStatus.Ready && order.Type == OrderType.Delivery
            ? OrderStatus.OutForDelivery
            : order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery
                ? OrderStatus.Completed
                : null;

    private static OrderRoutingStateDto ToRoutingDto(OrderRoutingState state) => new(
        state.Id, state.JobId, state.Revision, state.Target, state.Status, state.DeviceId,
        state.FailureReason, state.LastAcknowledgedAt, state.Version, state.IsRequired);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
