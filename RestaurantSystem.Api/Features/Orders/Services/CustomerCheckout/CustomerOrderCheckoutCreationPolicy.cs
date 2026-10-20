using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Rechecks current permission and accepted money before every native provider create/replay.</summary>
public sealed class CustomerOrderCheckoutCreationPolicy(ApplicationDbContext context, CustomMediator mediator,
    IOptions<CustomerHostedCheckoutSettings> options, ICheckoutChargeResolver charges,
    IOnlinePaymentIntentGuard intents) : ICustomerOrderCheckoutCreationPolicy
{
    public async Task<CheckoutCharge> ReadAsync(Order order, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.IsValid()) throw Unavailable();
        var bootstrap = await CustomerBasketAvailability.ReadOrderingAsync(mediator, cancellationToken);
        // Readiness stays unknown publicly until the complete native recovery flow is delivered.
        if (bootstrap.Capabilities.GetValueOrDefault("online-payments") is not ("unknown" or "enabled"))
            throw Unavailable();
        OnlinePaymentEligibility.EnsurePayable(order);
        await intents.EnsureProcessingAsync(order.Id, cancellationToken);
        var billing = await context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == order.Id, cancellationToken) ?? throw Unavailable();
        var charge = charges.Resolve(order.PayableTotal, billing.Currency);
        if (charge.Amount.Minor != billing.TotalMinor)
            throw new ConflictException("The accepted order changed. Ask the restaurant to review its payment.");
        return charge;
    }

    private static NotFoundException Unavailable() => new("Native payment operation unavailable.");
}
