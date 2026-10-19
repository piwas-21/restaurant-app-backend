using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.MutateCustomerBasketItemCommand;

public sealed record MutateCustomerBasketItemCommand(string SessionId, Guid BasketItemId,
    CustomerBasketItemMutationDto Request) : ICommand<ApiResponse<BasketDto>>;

public sealed class MutateCustomerBasketItemCommandHandler(ICustomerBasketItemMutationService mutations)
    : ICommandHandler<MutateCustomerBasketItemCommand, ApiResponse<BasketDto>>
{
    public async Task<ApiResponse<BasketDto>> Handle(MutateCustomerBasketItemCommand command,
        CancellationToken cancellationToken) =>
        ApiResponse<BasketDto>.SuccessWithData(await mutations.MutateAsync(command.SessionId,
            command.BasketItemId, command.Request, cancellationToken));
}
