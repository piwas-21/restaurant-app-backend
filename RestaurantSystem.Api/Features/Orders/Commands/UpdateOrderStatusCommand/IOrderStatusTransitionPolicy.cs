using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public interface IOrderStatusTransitionPolicy
{
    OrderWorkflowSettings Workflow { get; }

    bool RequiresKitchenBoardHandover(OrderStatus targetStatus);
}
