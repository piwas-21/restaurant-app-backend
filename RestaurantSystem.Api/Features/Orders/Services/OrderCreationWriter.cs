using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class OrderCreationWriter(ApplicationDbContext context, IOrderItemFactory items,
    IOrderPricingService pricing, IOrderPaymentBuilder payments, IOrderNativeBillingAcceptance fidelity,
    IOrderPreparationService preparation) : IOrderCreationWriter
{
    public async Task<string?> WriteAsync(CreateOrderCommand command, OrderDraft draft,
        string? acceptedCurrency, Func<Task>? beforeSave, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Order creation requires the caller's transaction.");
        if (draft.IsFailed) return draft.Error;
        var order = draft.Order;
        context.Orders.Add(order);
        foreach (var item in command.Items)
        {
            var error = await items.AddItemAsync(order, item, command.ItemsAreServerPriced, cancellationToken);
            if (error is not null) return error;
        }
        var itemsTotal = order.Items.Sum(item => item.ItemTotal);
        await pricing.ApplyAsync(order, itemsTotal, command, draft.UserId, cancellationToken);
        var earning = await fidelity.CalculatePointsToEarnAsync(order, itemsTotal, draft.UserId, cancellationToken);
        payments.AddPayments(order, command.Payments);
        payments.UpdatePaymentSummary(order);
        await preparation.ApplyForNewOrderAsync(order, draft.Now, cancellationToken);
        if (beforeSave is not null) await beforeSave();
        order.StatusHistory.Add(new OrderStatusHistory
        {
            FromStatus = OrderStatus.Pending,
            ToStatus = order.Status,
            Notes = OnlinePaymentIntent.InitialStatusNote(command.Type, order.TableNumber, draft.PaysOnline),
            ChangedAt = draft.Now,
            ChangedBy = draft.AuditId,
            CreatedAt = draft.Now,
            CreatedBy = draft.AuditId
        });
        await context.SaveChangesAsync(cancellationToken);
        var redemption = await fidelity.RedeemAsync(order, command.PointsToRedeem, draft.UserId, cancellationToken);
        await fidelity.WriteAcceptedSnapshotAsync(order, acceptedCurrency, earning, redemption, cancellationToken);
        // Only the server-computed payment status permits earned points to be awarded.
        await fidelity.AwardEarnedPointsAsync(order, cancellationToken);
        return null;
    }
}
