using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerOrderReceiptReader(ApplicationDbContext context, ICustomerCheckoutGuard guard,
    ICustomerOrderOperationStore operations, ITenantClock clock) : ICustomerOrderReceiptReader
{
    public async Task<CustomerOrderReceiptDto> ReadAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        var userId = guard.EnsureOwner(sessionId);
        var operation = await operations.RecoverAsync(sessionId, proof.OperationId, proof.PayloadHash, cancellationToken);
        if (operation.Outcome != CustomerOrderOperationOutcome.Applied || operation.OrderId is not { } orderId)
            throw Unavailable();
        // A retained operation never bypasses deleted/anonymized order ownership.
        var order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == orderId
            && value.UserId == userId && !value.IsDeleted, cancellationToken) ?? throw Unavailable();
        var currency = await context.OrderBillingSnapshots.AsNoTracking().Where(value => value.OrderId == orderId)
            .Select(value => value.Currency).SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        var items = await context.OrderItems.AsNoTracking().Where(item => item.OrderId == orderId)
            .OrderBy(item => item.PresentationOrder).ThenBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Take(CustomerCheckoutLimits.MaximumLines + 1).ToArrayAsync(cancellationToken);
        if (items.Length > CustomerCheckoutLimits.MaximumLines) throw Unsupported();
        var itemIds = items.Select(item => item.Id).ToArray();
        var ingredients = await context.Set<OrderItemIngredient>().AsNoTracking()
            .Where(ingredient => itemIds.Contains(ingredient.OrderItemId))
            .OrderBy(ingredient => ingredient.Id).Take(CustomerCheckoutLimits.MaximumIngredientLines + 1)
            .ToArrayAsync(cancellationToken);
        if (ingredients.Length > CustomerCheckoutLimits.MaximumIngredientLines) throw Unsupported();
        var byItem = ingredients.ToLookup(ingredient => ingredient.OrderItemId);
        foreach (var item in items) item.IngredientSnapshots = byItem[item.Id].ToArray();
        order.Items = items;
        var configuration = await context.OrderTypeConfigurations.AsNoTracking().Where(value => value.OrderType == order.Type)
            .Select(value => new { value.ConfirmationFlow, value.ReviewWindowMinutes }).SingleOrDefaultAsync(cancellationToken);
        var flow = configuration?.ConfirmationFlow ?? OrderTypeConfiguration.DefaultConfirmationFlow;
        var deadline = flow == OrderConfirmationFlows.Acknowledge
            ? order.OrderDate.AddMinutes(configuration?.ReviewWindowMinutes ?? OrderTypeConfiguration.DefaultReviewWindowMinutes)
            : (DateTime?)null;
        var money = new CustomerCheckoutMoneyDto(order.SubTotal, order.Tax, order.DeliveryFee, order.Discount,
            order.CustomerDiscountAmount, order.FidelityPointsDiscount, order.FidelityPointsRedeemed, order.Tip, order.Total);
        return new(1, order.Id, order.OrderNumber, order.Type, order.OrderDate, order.Status, order.PaymentStatus,
            currency, clock.TimeZone.Id, order.ExpectedReadyAt, order.EstimatedDeliveryTime, flow, deadline, money,
            order.PayableTotal, order.BillingCreditAmount, order.BillChargeAmount, order.TotalPaid,
            order.PayableTotal - order.TotalPaid, CustomerCheckoutComposition.Create(order));
    }

    private static NotFoundException Unavailable() => new("Order receipt unavailable.");
    private static ConflictException Unsupported() => new("This order receipt is too large. Ask the restaurant for help.");
}
