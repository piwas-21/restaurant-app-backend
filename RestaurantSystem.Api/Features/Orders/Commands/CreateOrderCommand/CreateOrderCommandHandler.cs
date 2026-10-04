using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
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
    private readonly IOrderNativeBillingAcceptance _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderPermittedActionsService _permittedActionsService;
    private readonly IOrderFactory _orderFactory;
    private readonly IPreferredLanguageCapture _languages;
    private readonly ITableGuestRoundOperationStore? _guestRounds;
    private readonly ITenantFeatures? _features;

    public CreateOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IOrderItemFactory itemFactory,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        IOrderTableReservationService tableReservation,
        IOrderNativeBillingAcceptance fidelity,
        IOrderNotificationService notifications,
        IOrderPermittedActionsService permittedActionsService,
        IOrderFactory orderFactory,
        IPreferredLanguageCapture languages,
        ILogger<CreateOrderCommandHandler> logger,
        ITableGuestRoundOperationStore? guestRounds = null,
        ITenantFeatures? features = null)
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
        _features = features;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var guestContext = command.GuestRoundContext;
        var validationFailure = GuestRoundOrderPolicy.ValidateSubmission(command, _features?.TableVisitReadinessV1 == true);
        if (validationFailure is not null) return validationFailure;

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

            var acceptedCurrency = await OrderNativeAcceptedCurrency.ResolveForAcceptanceAsync(
                _context, guestSession?.Currency, cancellationToken);

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

            var itemsTotal = order.Items.Sum(i => i.ItemTotal);
            await _pricingService.ApplyAsync(order, itemsTotal, command, userId, cancellationToken);

            var earning = await _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

            _paymentBuilder.AddPayments(order, command.Payments);
            _paymentBuilder.UpdatePaymentSummary(order);

            if (guestSession is not null && guestParticipant is not null)
            {
                GuestRoundOrderPolicy.RecordAccountChange(
                    _context, _guestRounds ?? throw new InvalidOperationException(
                        "Guest round operations are not registered."),
                    guestContext!, guestSession, guestParticipant, order);
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

            var redemption = await _fidelity.RedeemAsync(order, command.PointsToRedeem, userId, cancellationToken);
            await _fidelity.WriteAcceptedSnapshotAsync(
                order, acceptedCurrency, earning, redemption, cancellationToken);

            // Gated on the server-computed order.PaymentStatus: a caller cannot declare itself paid
            // into an award, and an online order is not paid yet — the settle path awards instead.
            await _fidelity.AwardEarnedPointsAsync(order, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);
            orderDto.PermittedActions = _permittedActionsService.GetPermittedActions(order);

            await _notifications.NotifyOrderCreatedAsync(orderDto);
            await _notifications.NotifyFocusOrderUpdateAsync(orderDto);
            await _tableReservation.ReserveForDineInAsync(order, cancellationToken);

            // Online orders receive confirmation mail only after Stripe settles them.
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
