using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Common.Exceptions;
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
    private readonly IOrderPermittedActionsService _permittedActionsService;
    private readonly IOrderFactory _orderFactory;
    private readonly IPreferredLanguageCapture _languages;
    private readonly ITableGuestRoundOperationStore? _guestRounds;

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
        IOrderPermittedActionsService permittedActionsService,
        IOrderFactory orderFactory,
        IPreferredLanguageCapture languages,
        ILogger<CreateOrderCommandHandler> logger,
        ITableGuestRoundOperationStore? guestRounds = null)
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
        _permittedActionsService = permittedActionsService;
        _logger = logger;
        _guestRounds = guestRounds;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // Validate guest context before entering the order-number transaction and advisory lock.
        var guestContext = command.GuestRoundContext;
        if (guestContext is not null)
        {
            GuestRoundOrderPolicy.Validate(command);
        }

        var ownerId = guestContext is null ? command.UserId ?? _currentUserService.UserId : null;
        var language = await _languages.ForUserAsync(ownerId, cancellationToken);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            TableServiceSession? guestSession = null;
            TableGuestParticipant? guestParticipant = null;
            if (guestContext is not null)
            {
                var guestRounds = _guestRounds ?? throw new InvalidOperationException(
                    "Guest round operations are not registered.");
                var preparation = await guestRounds.PrepareUnderLockAsync(guestContext, cancellationToken);
                if (preparation.Replay is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return preparation.Replay;
                }

                guestSession = preparation.Session;
                guestParticipant = preparation.Participant;
                command.TableNumber = guestSession.TableNumber;
            }

            var draft = await _orderFactory.CreateAsync(command, ownerId, language, cancellationToken);

            if (draft.IsFailed)
            {
                return ApiResponse<OrderDto>.Failure(draft.Error);
            }

            var order = draft.Order;
            if (guestSession is not null)
            {
                await GuestRoundOrderPolicy.AttachVisitAsync(
                    _context, order, guestSession, cancellationToken);
            }

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

            // Money is derived from server-resolved items; fidelity redemption is recomputed after save.
            var itemsTotal = order.Items.Sum(i => i.ItemTotal);
            await _pricingService.ApplyAsync(order, itemsTotal, command, userId, cancellationToken);

            await _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

            _paymentBuilder.AddPayments(order, command.Payments);
            _paymentBuilder.UpdatePaymentSummary(order);

            if (guestSession is not null && guestParticipant is not null && guestContext is not null)
            {
                GuestRoundOrderPolicy.RecordAccountChange(
                    _context, _guestRounds ?? throw new InvalidOperationException(
                        "Guest round operations are not registered."),
                    guestContext, guestSession, guestParticipant, order, auditId, now);
            }

            order.StatusHistory.Add(new OrderStatusHistory
            {
                FromStatus = OrderStatus.Pending,
                ToStatus = order.Status,
                Notes = OnlinePaymentIntent.InitialStatusNote(command.Type, order.TableNumber, paysOnline),
                ChangedAt = now,
                ChangedBy = auditId,
                CreatedAt = now,
                CreatedBy = auditId,
            });

            await _context.SaveChangesAsync(cancellationToken);

            // Redemption has an order FK, so it must happen after SaveChangesAsync.
            await _fidelity.RedeemAsync(order, command.PointsToRedeem, userId, cancellationToken);

            // Gated on the server-computed order.PaymentStatus: a caller cannot declare itself paid
            // into an award, and an online order is not paid yet — the settle path awards instead.
            await _fidelity.AwardEarnedPointsAsync(order, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);
            orderDto.PermittedActions = _permittedActionsService.GetPermittedActions(order);

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
