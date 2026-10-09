using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;
using LegacyCreateOrderCommand = RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand.CreateOrderCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;

public class CreateOrderFromBasketCommandHandler
    : ICommandHandler<CreateOrderFromBasketCommand, ApiResponse<OrderDto>>
{
    private readonly IBasketService _basketService;
    private readonly IBasketToOrderTranslator _translator;
    private readonly ICurrentUserService _currentUserService;
    private readonly CustomMediator _mediator;
    private readonly IOrderTypeConfigurationService _orderTypeConfigurationService;
    private readonly ITableGuestRoundOperationStore? _guestRounds;

    public CreateOrderFromBasketCommandHandler(
        IBasketService basketService,
        IBasketToOrderTranslator translator,
        ICurrentUserService currentUserService,
        CustomMediator mediator,
        IOrderTypeConfigurationService orderTypeConfigurationService,
        ITableGuestRoundOperationStore? guestRounds = null)
    {
        _basketService = basketService;
        _translator = translator;
        _currentUserService = currentUserService;
        _mediator = mediator;
        _orderTypeConfigurationService = orderTypeConfigurationService;
        _guestRounds = guestRounds;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        CreateOrderFromBasketCommand command, CancellationToken cancellationToken)
    {
        if (command.GuestRoundContext is not null)
        {
            var replay = await (_guestRounds ?? throw new InvalidOperationException(
                "Guest round operations are not registered.")).FindReplayBeforeBasketAsync(
                    command.GuestRoundContext, cancellationToken);
            if (replay is not null)
            {
                return replay;
            }
        }

        if (command.GuestRoundContext is not null)
        {
            var enabledOrderTypes = await _orderTypeConfigurationService.GetEnabledOrderTypesAsync(cancellationToken);
            if (!enabledOrderTypes.Contains(OrderType.DineIn))
            {
                throw new BadRequestException(
                    "Dine-in ordering is currently unavailable. Your table visit and basket are still saved. Try again later or ask staff.",
                    ErrorCodes.OrderTypeNotAvailable);
            }
        }

        var basket = await _basketService.GetBasketAsync(command.SessionId, _currentUserService.UserId);
        if (basket is null)
        {
            if (command.GuestRoundContext is not null)
            {
                throw BasketChanged();
            }

            throw new BadRequestException("Cannot create an order from an empty basket.");
        }

        if (command.GuestRoundContext is not null
            && !BasketPurchaseFingerprint.Matches(
                command.GuestRoundContext.ExpectedBasketFingerprint, basket.PurchaseFingerprint))
        {
            throw BasketChanged();
        }

        if (basket.Items is not { Count: > 0 })
        {
            // 400, matching the legacy path's empty-Items rejection (CreateOrderCommandValidator)
            // so this new public surface has a single, consistent order-error contract.
            throw new BadRequestException("Cannot create an order from an empty basket.");
        }

        // The channel the basket was PRICED for must be the channel the order is placed on.
        // BasketPricingService keys the delivery fee on basket.OrderType while OrderPricingService
        // keys it on command.Type, so a basket pinned to DineIn and checked out as Delivery gets a
        // fee the customer was never shown — and since the client also derives its tender amount
        // from basket.total, the gap lands as a silently PartiallyPaid order rather than an error.
        // Inert while the fee defaults to 0; refused here so it stays inert when a tenant sets one.
        if (basket.OrderType.HasValue && basket.OrderType.Value != command.Type)
        {
            throw new BadRequestException(
                "The order type does not match the basket's. Reselect it and check out again.");
        }

        // Delegate to the untouched legacy command — the server-derived Items replace what the
        // client used to hand-build. The delegated handler runs through the full CustomMediator
        // pipeline (validation included), so behaviour is identical to a direct POST /api/orders.
        var createOrder = new LegacyCreateOrderCommand
        {
            CustomerName = command.CustomerName,
            CustomerEmail = command.CustomerEmail,
            CustomerPhone = command.CustomerPhone,
            Type = command.Type,
            TableNumber = command.TableNumber,
            PromoCode = command.PromoCode,
            PointsToRedeem = command.PointsToRedeem,
            Tip = command.Tip ?? 0m,
            Notes = command.Notes,
            DeliveryAddress = command.DeliveryAddress,
            Items = _translator.Translate(basket.Items),
            // These items came from the persisted basket, so their UnitPrice already carries the
            // bundle roll-up and variation modifier the catalogue price alone cannot express.
            ItemsAreServerPriced = true,
            GuestRoundContext = command.GuestRoundContext,
            Payments = command.Payments,
            // UserId and staff/POS-only fields (focus order, user-limit discount) are left at their
            // CreateOrderCommand defaults — the basket-checkout flow never sets them (UserId falls
            // back to the current user inside the delegated handler).
        };

        return await _mediator.SendCommand(createOrder, cancellationToken);
    }

    private static BadRequestException BasketChanged() => new(
        "The basket changed since it was reviewed. Refresh the table account before sending this round.",
        ErrorCodes.TableServiceSessionStale);
}
