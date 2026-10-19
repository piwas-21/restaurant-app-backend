using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketChannelCommand;

public sealed record CancelCustomerBasketChannelCommand(string SessionId, CustomerBasketChannelDto Request)
    : ICommand<ApiResponse<CustomerBasketOperationResolutionDto>>;

public sealed class CancelCustomerBasketChannelCommandHandler(ICustomerBasketOperationService operations)
    : ICommandHandler<CancelCustomerBasketChannelCommand, ApiResponse<CustomerBasketOperationResolutionDto>>
{
    public async Task<ApiResponse<CustomerBasketOperationResolutionDto>> Handle(CancelCustomerBasketChannelCommand command,
        CancellationToken cancellationToken) =>
        ApiResponse<CustomerBasketOperationResolutionDto>.SuccessWithData(await operations.CancelChannelAsync(command.SessionId,
            command.Request, cancellationToken));
}
