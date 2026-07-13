using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using LegacyCreateOrderCommand = RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand.CreateOrderCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;

public class CreateOrderFromBasketCommandHandler
    : ICommandHandler<CreateOrderFromBasketCommand, ApiResponse<OrderDto>>
{
    private readonly IBasketService _basketService;
    private readonly IBasketToOrderTranslator _translator;
    private readonly ICurrentUserService _currentUserService;
    private readonly CustomMediator _mediator;

    public CreateOrderFromBasketCommandHandler(
        IBasketService basketService,
        IBasketToOrderTranslator translator,
        ICurrentUserService currentUserService,
        CustomMediator mediator)
    {
        _basketService = basketService;
        _translator = translator;
        _currentUserService = currentUserService;
        _mediator = mediator;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        CreateOrderFromBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await _basketService.GetBasketAsync(command.SessionId, _currentUserService.UserId);
        if (basket == null || basket.Items.Count == 0)
        {
            // 400, matching the legacy path's empty-Items rejection (CreateOrderCommandValidator)
            // so this new public surface has a single, consistent order-error contract.
            throw new BadRequestException("Cannot create an order from an empty basket.");
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
            BasketSubTotal = command.BasketSubTotal,
            BasketTax = command.BasketTax,
            BasketDiscount = command.BasketDiscount,
            BasketCustomerDiscount = command.BasketCustomerDiscount,
            BasketTotal = command.BasketTotal,
            PointsToRedeem = command.PointsToRedeem,
            Tip = command.Tip ?? 0m,
            Notes = command.Notes,
            DeliveryAddress = command.DeliveryAddress,
            Items = _translator.Translate(basket.Items),
            Payments = command.Payments,
            // UserId and staff/POS-only fields (focus order, user-limit discount) are left at their
            // CreateOrderCommand defaults — the basket-checkout flow never sets them (UserId falls
            // back to the current user inside the delegated handler).
        };

        return await _mediator.SendCommand(createOrder, cancellationToken);
    }
}
