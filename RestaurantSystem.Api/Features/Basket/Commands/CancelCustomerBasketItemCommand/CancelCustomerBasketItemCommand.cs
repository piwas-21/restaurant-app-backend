using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketItemCommand;

public sealed record CancelCustomerBasketItemCommand(string SessionId, Guid BasketItemId,
    CustomerBasketItemMutationDto Request) : ICommand<ApiResponse<CustomerBasketOperationResolutionDto>>;

public sealed class CancelCustomerBasketItemCommandHandler(ICustomerBasketOperationService operations)
    : ICommandHandler<CancelCustomerBasketItemCommand, ApiResponse<CustomerBasketOperationResolutionDto>>
{
    public async Task<ApiResponse<CustomerBasketOperationResolutionDto>> Handle(CancelCustomerBasketItemCommand command,
        CancellationToken cancellationToken) =>
        ApiResponse<CustomerBasketOperationResolutionDto>.SuccessWithData(
            await operations.CancelMutationAsync(command.SessionId, command.BasketItemId, command.Request, cancellationToken));
}
