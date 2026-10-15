using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public interface IOrderStatusUpdateServices
{
    IOrderPreparationService Preparation { get; }
    IOrderStatusTransitionPolicy? TransitionPolicy { get; }
}

internal sealed class OrderStatusUpdateServices(
    IOrderPreparationService preparation,
    IOrderStatusTransitionPolicy? transitionPolicy) : IOrderStatusUpdateServices
{
    public IOrderPreparationService Preparation { get; } = preparation;
    public IOrderStatusTransitionPolicy? TransitionPolicy { get; } = transitionPolicy;
}
