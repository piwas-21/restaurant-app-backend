using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.SwitchCustomerBasketChannelCommand;

public sealed record SwitchCustomerBasketChannelCommand(string SessionId, CustomerBasketChannelDto Request)
    : ICommand<ApiResponse<BasketDto>>;

public sealed class SwitchCustomerBasketChannelCommandHandler(ICustomerBasketChannelService operations)
    : ICommandHandler<SwitchCustomerBasketChannelCommand, ApiResponse<BasketDto>>
{
    public async Task<ApiResponse<BasketDto>> Handle(SwitchCustomerBasketChannelCommand command,
        CancellationToken cancellationToken) =>
        ApiResponse<BasketDto>.SuccessWithData(await operations.SwitchAsync(command.SessionId,
            command.Request, cancellationToken));
}
