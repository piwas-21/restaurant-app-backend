using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc cref="IOrderFactory"/>
public class OrderFactory : IOrderFactory
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderChannelGuard _channelGuard;
    private readonly IOrderNumberGenerator _orderNumbers;
    private readonly IOrderAddressFactory _addressFactory;

    public OrderFactory(
        ICurrentUserService currentUserService,
        IOrderChannelGuard channelGuard,
        IOrderNumberGenerator orderNumbers,
        IOrderAddressFactory addressFactory)
    {
        _currentUserService = currentUserService;
        _channelGuard = channelGuard;
        _orderNumbers = orderNumbers;
        _addressFactory = addressFactory;
    }

    public async Task<OrderDraft> CreateAsync(
        CreateOrderCommand command, Guid? userId, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Non-null only when a staff member was warned and allowed through anyway (§9.6).
        var channelOverride = await _channelGuard.EnsureOrderableAsync(command.Items, command.Type, cancellationToken);

        var orderNumber = await _orderNumbers.GenerateAsync(cancellationToken);
        var auditId = _currentUserService.GetAuditIdentifier();
        var now = DateTime.UtcNow;
        // Holds Dine-in at Pending so an unpaid order never reaches the kitchen feed.
        var paysOnline = OnlinePaymentIntent.IsDeclaredIn(command.Payments);

        var order = new Order
        {
            OrderNumber = orderNumber,
            // Minted for every order, not just the ones that trigger an admin email: which
            // orders get mailed is a runtime decision made later and elsewhere, and an order
            // that reaches the template without a token would render dead links.
            QuickActionToken = QuickActionTokens.Generate(),
            UserId = userId,
            CustomerName = command.CustomerName,
            CustomerEmail = command.CustomerEmail,
            CustomerPhone = command.CustomerPhone,
            // Frozen here, once, so every later mail about this order — the receipt, a status
            // change, a resend days later through a quick-action link — reads in the language the
            // guest ordered in, even though those paths have no request of their own to ask
            // (EMAIL-LOCALISATION-PLAN §1 rank 1, §6.5). Resolved by the handler BEFORE the
            // transaction opens: the order-number generator holds a day-wide advisory lock from
            // here on, and a language lookup has no business inside it — nor any business being a
            // new way for an order to fail.
            PreferredLanguage = language,
            Type = command.Type,
            TableNumber = command.TableNumber,
            PromoCode = command.PromoCode,
            HasUserLimitDiscount = command.HasUserLimitDiscount,
            UserLimitAmount = command.UserLimitAmount,
            // Priority and FocusReason used to be copied in unconditionally, so an unfocused
            // order could carry both; they now travel with the focus record or not at all.
            Focus = command.IsFocusOrder
                ? new OrderFocus
                {
                    Priority = command.Priority,
                    Reason = command.FocusReason,
                    FocusedAt = now,
                    FocusedBy = userId?.ToString()
                }
                : null,
            OrderTypeOverrideBy = channelOverride?.By,
            OrderTypeOverrideItems = channelOverride?.Items,
            Notes = command.Notes,
            OrderDate = now,
            Tip = command.Tip,
            Status = OnlinePaymentIntent.InitialStatus(command.Type, paysOnline),
            PaymentStatus = PaymentStatus.Pending,
            EstimatedDeliveryTime = command.Type == OrderType.Delivery ? now.AddMinutes(45) : null,
            CreatedAt = now,
            CreatedBy = auditId,
        };

        if (command.Type == OrderType.Delivery)
        {
            var orderAddress = await _addressFactory.CreateAsync(
                command.DeliveryAddress, order.Id, userId, cancellationToken);

            if (orderAddress == null)
            {
                return OrderDraft.Failed("Delivery address is required for delivery orders");
            }

            order.DeliveryAddress = orderAddress;
        }

        return new OrderDraft(order, null, userId, auditId, now, paysOnline);
    }
}
