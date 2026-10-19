using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketAddCommand;

public sealed record CancelCustomerBasketAddCommand(string SessionId, AddToBasketDto Request)
    : ICommand<ApiResponse<CustomerBasketOperationResolutionDto>>;

public sealed class CancelCustomerBasketAddCommandHandler(ICustomerBasketOperationService operations)
    : ICommandHandler<CancelCustomerBasketAddCommand, ApiResponse<CustomerBasketOperationResolutionDto>>
{
    public async Task<ApiResponse<CustomerBasketOperationResolutionDto>> Handle(CancelCustomerBasketAddCommand command,
        CancellationToken cancellationToken) =>
        ApiResponse<CustomerBasketOperationResolutionDto>.SuccessWithData(
            await operations.CancelAddAsync(command.SessionId, command.Request, cancellationToken));
}
