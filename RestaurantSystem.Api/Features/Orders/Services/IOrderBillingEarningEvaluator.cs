namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderBillingEarningEvaluator
{
    Task<OrderBillingEarningEvaluation> EvaluateAsync(
        decimal rawRootTotal,
        CancellationToken cancellationToken = default);
}
