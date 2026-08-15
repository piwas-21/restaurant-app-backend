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
    private readonly IOrderItemFactory _itemFactory;
    private readonly IOrderPricingService _pricingService;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly IOrderTableReservationService _tableReservation;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderFactory _orderFactory;
    private readonly IPreferredLanguageCapture _languages;

    public CreateOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IOrderItemFactory itemFactory,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        IOrderTableReservationService tableReservation,
        IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications,
        IOrderFactory orderFactory,
        IPreferredLanguageCapture languages,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderFactory = orderFactory;
        _languages = languages;
        _mappingService = mappingService;
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
        // Before the transaction on purpose: everything after this line runs behind the order-number
        // generator's day-wide advisory lock, and a failed statement inside a transaction poisons it,
        // so a lookup for a cosmetic field would become a way to lose an order (GAP-2 S4 review).
        var ownerId = command.UserId ?? _currentUserService.UserId;
        var language = await _languages.ForUserAsync(ownerId, cancellationToken);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var draft = await _orderFactory.CreateAsync(command, ownerId, language, cancellationToken);

            if (draft.IsFailed)
            {
                return ApiResponse<OrderDto>.Failure(draft.Error);
            }

            var order = draft.Order;
            var userId = draft.UserId;
            var auditId = draft.AuditId;
            var now = draft.Now;
            var paysOnline = draft.PaysOnline;

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
            // Before the mail: mail latency in front of it widens the window in which a process
            // death leaves a dine-in order with no table.
            await _tableReservation.ReserveForDineInAsync(order, cancellationToken);

            // The mail is a consequence of the order existing, not of the guest's tab staying
            // open (GAP-11). An online order is excluded — held Pending above, it owes nobody a
            // confirmation until Stripe reports the money; the settle path mails it then.
            if (!paysOnline)
            {
                await _notifications.SendNewOrderMailAsync(order, orderDto, cancellationToken);
            }

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
