using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentKitchenStager
{
    private readonly IOrderKitchenChangeWriter _writer;
    private readonly IOrderRoutingReadinessSnapshotProvider _routingSnapshot;

    public OrderAmendmentKitchenStager(
        IOrderKitchenChangeWriter writer,
        IOrderRoutingReadinessSnapshotProvider routingSnapshot)
    {
        _writer = writer;
        _routingSnapshot = routingSnapshot;
    }

    internal async Task StageAsync(
        Order source,
        Guid amendmentId,
        long? accountRevision,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        CancellationToken cancellationToken)
    {
        if (!CanPrintCorrection(source))
            return;

        var routing = await _routingSnapshot.LoadAsync(cancellationToken);
        var entities = source.Items.ToDictionary(item => item.Id);
        var byTarget = new Dictionary<DevicePrintTarget, List<PrinterFeedChangeDto>>();
        foreach (var change in changes)
        {
            if (!entities.TryGetValue(change.OrderItemId, out var item))
                continue;
            var printerChange = ToPrinterChange(change);
            if (printerChange is null)
                continue;

            foreach (var target in OrderRoutingTargetResolver
                         .ResolveItemTargets(item, routing.RoutingMode)
                         .Where(target => target != DevicePrintTarget.Cashier).Distinct())
            {
                if (!byTarget.TryGetValue(target, out var targetChanges))
                    byTarget.Add(target, targetChanges = []);
                targetChanges.Add(printerChange);
            }
        }

        foreach (var (target, targetChanges) in byTarget.OrderBy(pair => pair.Key))
        {
            await _writer.StageAsync(
                source,
                amendmentId,
                accountRevision,
                target,
                targetChanges,
                $"Order amendment {amendmentId:N}: {targetChanges.Count} source-line correction(s).",
                cancellationToken);
        }
    }

    private static bool CanPrintCorrection(Order source) => source.IsKitchenReleased
        && source.Status is OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready;

    private static PrinterFeedChangeDto? ToPrinterChange(OrderAmendmentChangeSnapshot change)
    {
        if (change.Kind == OrderAmendmentChangeKind.Replace)
        {
            if (change.ReplacementDispatchedOrderId is Guid replacementOrderId
                && !string.IsNullOrWhiteSpace(change.ReplacementDispatchedOrderNumber))
            {
                return new PrinterFeedChangeDto
                {
                    Kind = KitchenChangeKind.Replace,
                    Previous = change.Previous,
                    Current = change.Current,
                    ReplacementDispatchedOrderId = replacementOrderId,
                    ReplacementDispatchedOrderNumber = change.ReplacementDispatchedOrderNumber
                };
            }

            // A held replacement still cancels the stale released line, but its new item must
            // wait for the ordinary supplement release and must not appear as a kitchen action.
            return new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Void,
                Previous = change.Previous
            };
        }

        return change.Kind switch
        {
            OrderAmendmentChangeKind.Void => new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Void,
                Previous = change.Previous
            },
            OrderAmendmentChangeKind.InstructionChange => new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.InstructionChange,
                Previous = change.Previous,
                Current = change.Current
            },
            _ => null
        };
    }
}
