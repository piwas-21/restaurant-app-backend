using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutDraft(ICustomerCheckoutItems items, IOrderPricingService pricing,
    IOrderFidelityCoordinator fidelity, ICurrentUserService currentUser) : ICustomerCheckoutDraft
{
    public async Task<Order> BuildAsync(CustomerCheckoutReviewContext context, CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken) => (await PrepareAsync(context, review, cancellationToken)).Order;

    public async Task<CustomerCheckoutPrepared> PrepareAsync(CustomerCheckoutReviewContext context, CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = context.UserId,
            Type = review.Type,
            Tip = review.Tip,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        var prepared = await items.PopulateAsync(order, context.Basket, review.Type, cancellationToken);
        var command = new CreateOrderCommand { Type = review.Type, Tip = review.Tip };
        await pricing.PreviewAsync(order, order.Items.Sum(item => item.ItemTotal), command, context.UserId, cancellationToken);
        await fidelity.PreviewRedemptionAsync(order, review.PointsToRedeem, context.UserId, cancellationToken);
        CustomerCheckoutMoneyGuard.Ensure(order);
        return new(order, prepared);
    }
}
