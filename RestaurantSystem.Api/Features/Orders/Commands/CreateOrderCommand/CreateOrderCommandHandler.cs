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
            var orderNumber = await GenerateOrderNumber(cancellationToken);
            var userId = command.UserId ?? _currentUserService.UserId;
            var auditId = _currentUserService.GetAuditIdentifier();
            var now = DateTime.UtcNow;

            var order = new Order
            {
                OrderNumber = orderNumber,
                UserId = userId,
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
                FocusedAt = command.IsFocusOrder ? now : null,
                FocusedBy = command.IsFocusOrder ? userId?.ToString() : null,
                Notes = command.Notes,
                OrderDate = now,
                Tip = command.Tip,
                // Auto-confirm Dine-in orders, keep others as Pending.
                Status = command.Type == OrderType.DineIn ? OrderStatus.Confirmed : OrderStatus.Pending,
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

            order.StatusHistory.Add(new OrderStatusHistory
            {
                FromStatus = OrderStatus.Pending,
                ToStatus = order.Status,
                Notes = command.Type == OrderType.DineIn ? "Order created and auto-confirmed (Dine-in)" : "Order created",
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

            // Cash payments stay Pending; points are awarded at
            // payment-completion time, not order-creation time. The
            // coordinator gates on PaymentStatus internally.
            await _fidelity.AwardEarnedPointsAsync(order, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

            await _notifications.NotifyOrderCreatedAsync(orderDto);
            await _notifications.NotifyFocusOrderUpdateAsync(orderDto);

            // Dine-in auto-confirms, so the confirmed-email goes synchronously.
            // Takeaway/Delivery defer to the /send-confirmation-email endpoint.
            if (command.Type == OrderType.DineIn)
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
