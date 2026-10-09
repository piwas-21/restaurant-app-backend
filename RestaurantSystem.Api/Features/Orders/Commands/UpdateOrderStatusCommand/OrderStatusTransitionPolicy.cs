using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public sealed class OrderStatusTransitionPolicy(
    IOptions<OrderWorkflowSettings> workflow,
    ITenantFeatures features) : IOrderStatusTransitionPolicy
{
    public OrderWorkflowSettings Workflow => workflow.Value;

    public bool RequiresKitchenBoardHandover(OrderStatus targetStatus) =>
        targetStatus is OrderStatus.OutForDelivery or OrderStatus.Completed
        && KitchenBoardFeaturePolicy.IsEnabled(features);
}
