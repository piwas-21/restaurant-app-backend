using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerOrderCheckoutEffects(ApplicationDbContext context, IFidelityPointsService fidelity,
    IOrderMappingService mapping, IGuestOrderReceiptSender receipts, IAdminOrderAlertSender alerts,
    TimeProvider clock) : ICustomerOrderCheckoutEffects
{
    public async Task CompleteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Native payment effects must run after the evidence commit.");
        try
        {
            var paid = await context.OrderCheckoutSessions.AsNoTracking().AnyAsync(value => value.OrderId == orderId
                && value.Status == CheckoutSessionStatus.Completed && value.ReconciledAt != null, cancellationToken);
            if (!paid) return;
            var order = await context.Orders.AsNoTracking().IncludeOrderLineGraph().Include(value => value.Payments)
                .Include(value => value.User).Include(value => value.DeliveryAddress).AsSplitQuery()
                .SingleOrDefaultAsync(value => value.Id == orderId && !value.IsDeleted, cancellationToken);
            var completed = order is null || !OrderStatusTransitions.IsValid(order.Status, OrderStatus.Cancelled);
            if (!completed && order is not null)
            {
                var view = await mapping.MapToOrderDtoAsync(order, cancellationToken);
                alerts.Queue(view);
                receipts.Queue(view);
                // Queue mail independently of loyalty conflicts; the strict award remains retryable.
                var award = order.UserId.HasValue
                    ? await fidelity.AwardAcceptedOrderAsync(orderId, cancellationToken) : null;
                // Queueing and an in-flight claim are not delivery evidence. Keep the journal
                // retryable until the existing ledgers report success, including after process death.
                var adminSent = await WasSentAsync(OutboundEmailTypes.OrderAdminAlert, orderId, cancellationToken);
                var guestSent = string.IsNullOrWhiteSpace(view.CustomerEmail)
                    || await WasSentAsync(OutboundEmailTypes.OrderReceived, orderId, cancellationToken);
                completed = adminSent && guestSent && award?.Disposition != FidelityPointsAwardDisposition.Deferred;
            }
            if (completed)
                await context.Set<CustomerOrderCheckoutJournal>().Where(value => value.OrderId == orderId)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.EffectsCompletedAt, clock.GetUtcNow().UtcDateTime), cancellationToken);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    private Task<bool> WasSentAsync(string type, Guid orderId, CancellationToken cancellationToken) =>
        context.OutboundEmails.AsNoTracking().AnyAsync(value => value.EmailType == type && value.EntityId == orderId
            && value.SentAt != null, cancellationToken);
}
