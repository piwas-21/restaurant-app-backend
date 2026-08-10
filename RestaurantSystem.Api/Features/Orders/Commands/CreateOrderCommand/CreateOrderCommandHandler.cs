using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly IOrderAddressFactory _addressFactory;
    private readonly IOrderItemFactory _itemFactory;
    private readonly IOrderPricingService _pricingService;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly IOrderTableReservationService _tableReservation;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderChannelGuard _channelGuard;
    private readonly IOrderNumberGenerator _orderNumbers;

    public CreateOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IOrderAddressFactory addressFactory,
        IOrderItemFactory itemFactory,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        IOrderTableReservationService tableReservation,
        IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications,
        IOrderChannelGuard channelGuard,
        IOrderNumberGenerator orderNumbers,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _channelGuard = channelGuard;
        _orderNumbers = orderNumbers;
        _mappingService = mappingService;
        _addressFactory = addressFactory;
        _itemFactory = itemFactory;
        _pricingService = pricingService;
        _paymentBuilder = paymentBuilder;
        _tableReservation = tableReservation;
        _fidelity = fidelity;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Non-null only when a staff member was warned and allowed through anyway (§9.6).
            var channelOverride = await _channelGuard.EnsureOrderableAsync(command.Items, command.Type, cancellationToken);

            var orderNumber = await _orderNumbers.GenerateAsync(cancellationToken);
            var userId = command.UserId ?? _currentUserService.UserId;
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
                var orderAddress = await _addressFactory.CreateAsync(command.DeliveryAddress, order.Id, userId, cancellationToken);
                if (orderAddress == null)
                {
                    return ApiResponse<OrderDto>.Failure("Delivery address is required for delivery orders");
                }
                order.DeliveryAddress = orderAddress;
            }

            _context.Orders.Add(order);

            foreach (var itemDto in command.Items)
            {
                var error = await _itemFactory.AddItemAsync(
                    order, itemDto, command.ItemsAreServerPriced, cancellationToken);
                if (error != null)
                {
                    return ApiResponse<OrderDto>.Failure(error);
                }
            }

            // Every money field is derived from these server-resolved items, never from the request
            // body (S0b). FidelityPointsDiscount is still 0 — redemption needs the order to exist,
            // so Total is recomputed after the save below.
            var itemsTotal = order.Items.Sum(i => i.ItemTotal);
            await _pricingService.ApplyAsync(order, itemsTotal, command, userId, cancellationToken);

            await _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

            _paymentBuilder.AddPayments(order, command.Payments);
            _paymentBuilder.UpdatePaymentSummary(order);

            order.StatusHistory.Add(new OrderStatusHistory
            {
                FromStatus = OrderStatus.Pending,
                ToStatus = order.Status,
                Notes = OnlinePaymentIntent.InitialStatusNote(command.Type, paysOnline),
                ChangedAt = now,
                ChangedBy = auditId,
                CreatedAt = now,
                CreatedBy = auditId,
            });

            await _context.SaveChangesAsync(cancellationToken);

            // Redemption must happen after SaveChangesAsync — the redemption
            // transaction has a FK to the order, which doesn't exist in the
            // DB until the save above.
            await _fidelity.RedeemAsync(order, command.PointsToRedeem, userId, cancellationToken);

            // Gated on the server-computed order.PaymentStatus: a caller cannot declare itself paid
            // into an award, and an online order is not paid yet — the settle path awards instead.
            await _fidelity.AwardEarnedPointsAsync(order, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

            await _notifications.NotifyOrderCreatedAsync(orderDto);
            await _notifications.NotifyFocusOrderUpdateAsync(orderDto);

            // Dine-in auto-confirms, so the confirmed-email goes synchronously; Takeaway/Delivery
            // defer to /send-confirmation-email. An online order confirms at neither — it was held
            // Pending above, and the settle path sends this same email once Stripe reports payment.
            if (command.Type == OrderType.DineIn && !paysOnline)
            {
                await _notifications.SendOrderConfirmedAsync(
                    order, OrderNotificationService.DefaultDineInPreparationMinutes, cancellationToken);
            }

            await _tableReservation.ReserveForDineInAsync(order, cancellationToken);

            _logger.LogInformation("Order {OrderNumber} created successfully by user {UserId}",
                order.OrderNumber, _currentUserService.UserId);

            return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order created successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }
}
