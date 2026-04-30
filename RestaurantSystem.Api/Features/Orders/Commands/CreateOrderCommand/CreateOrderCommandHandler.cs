using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
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
    private readonly IOrderEventService _orderEventService;
    private readonly IOrderMappingService _mappingService;
    private readonly IOrderAddressFactory _addressFactory;
    private readonly IOrderItemFactory _itemFactory;
    private readonly IOrderPricingService _pricingService;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly IOrderTableReservationService _tableReservation;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;

    public CreateOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        IOrderAddressFactory addressFactory,
        IOrderItemFactory itemFactory,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        IOrderTableReservationService tableReservation,
        IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderEventService = orderEventService;
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
            // Generate order number
            var orderNumber = await GenerateOrderNumber(cancellationToken);

            var userId = command.UserId ?? _currentUserService.UserId;

            // Create order
            var order = new Order
            {
                OrderNumber = orderNumber,
                UserId = command.UserId ?? _currentUserService.UserId,
                CustomerName = command.CustomerName,
                CustomerEmail = command.CustomerEmail,
                CustomerPhone = command.CustomerPhone,
                Type = command.Type,
                TableNumber = command.TableNumber,
                PromoCode = command.PromoCode,
                HasUserLimitDiscount = command.HasUserLimitDiscount,
                UserLimitAmount = command.UserLimitAmount,
                IsFocusOrder = command.IsFocusOrder,
                Priority = command.Priority,
                FocusReason = command.FocusReason,
                FocusedAt = command.IsFocusOrder ? DateTime.UtcNow : null,
                FocusedBy = command.IsFocusOrder ? _currentUserService.UserId?.ToString() : null,
                Notes = command.Notes,
                OrderDate = DateTime.UtcNow,
                Tip = command.Tip,
                // Auto-confirm Dine-in orders, keep others as Pending
                Status = command.Type == OrderType.DineIn ? OrderStatus.Confirmed : OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
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

            // Process order items and calculate totals
            foreach (var itemDto in command.Items)
            {
                var error = await _itemFactory.AddItemAsync(order, itemDto, cancellationToken);
                if (error != null)
                {
                    return ApiResponse<OrderDto>.Failure(error);
                }
            }

            // Aggregate item totals and apply pricing (tax, discounts, total).
            // FidelityPointsDiscount is 0 here; redemption (after SaveChangesAsync)
            // updates it separately and the persisted Total is not recomputed —
            // pre-existing behaviour, preserved verbatim.
            var itemsTotal = order.Items.Sum(i => i.ItemTotal);
            await _pricingService.ApplyAsync(order, itemsTotal, command, userId, cancellationToken);

            await _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

            _paymentBuilder.AddPayments(order, command.Payments);
            _paymentBuilder.UpdatePaymentSummary(order);

            // Add initial status history
            // Add order status history
            var statusHistory = new OrderStatusHistory
            {
                FromStatus = OrderStatus.Pending,
                ToStatus = order.Status,
                Notes = command.Type == OrderType.DineIn ? "Order created and auto-confirmed (Dine-in)" : "Order created",
                ChangedAt = DateTime.UtcNow,
                ChangedBy = _currentUserService.GetAuditIdentifier(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            order.StatusHistory.Add(statusHistory);

            // Calculate estimated delivery time
            if (command.Type == OrderType.Delivery)
            {
                order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(45); // Example: 45 minutes
            }

            await _context.SaveChangesAsync(cancellationToken);

            // Redemption must happen after SaveChangesAsync — the redemption
            // transaction has a FK to the order, which doesn't exist in the
            // DB until the save above.
            await _fidelity.RedeemAsync(order, command.PointsToRedeem, userId, cancellationToken);

            // Cash payments stay Pending; points are awarded at
            // payment-completion time, not order-creation time. The
            // coordinator gates on PaymentStatus internally.
            await _fidelity.AwardEarnedPointsAsync(order, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Map to DTO
            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

            // Notify clients via SSE - wrap in try-catch to ensure order creation succeeds even if notification fails
            try
            {
                _logger.LogInformation("Attempting to notify clients of order creation: {OrderNumber}", order.OrderNumber);
                await _orderEventService.NotifyOrderCreated(orderDto);
                _logger.LogInformation("Successfully notified clients of order creation: {OrderNumber}", order.OrderNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify clients of order creation for {OrderNumber}, but order was created successfully",
                    order.OrderNumber);
            }

            if (order.IsFocusOrder)
            {
                try
                {
                    await _orderEventService.NotifyFocusOrderUpdate(orderDto);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to notify clients of focus order update for {OrderNumber}", order.OrderNumber);
                }
            }

            // Auto-confirmed orders (Dine-in) send the confirmed-email synchronously here.
            // Other order types defer to the /send-confirmation-email endpoint.
            if (command.Type == OrderType.DineIn)
            {
                await _notifications.SendOrderConfirmedAsync(
                    order, OrderNotificationService.DefaultDineInPreparationMinutes, cancellationToken);
            }

            await _tableReservation.ReserveForDineInAsync(order, cancellationToken);
            // NOTE: For Takeaway and Delivery orders, email sending has been moved to the explicit /send-confirmation-email endpoint
            // This prevents duplicate emails and gives the frontend control over when emails are sent

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

    private async Task<string> GenerateOrderNumber(CancellationToken cancellationToken)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var lastOrder = await _context.Orders
            .Where(o => o.OrderNumber.StartsWith(date))
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = 1;
        if (lastOrder != null)
        {
            var lastSequence = lastOrder.OrderNumber.Substring(8);
            if (int.TryParse(lastSequence, out var seq))
            {
                sequence = seq + 1;
            }
        }

        return $"{date}{sequence:D4}";
    }
}
