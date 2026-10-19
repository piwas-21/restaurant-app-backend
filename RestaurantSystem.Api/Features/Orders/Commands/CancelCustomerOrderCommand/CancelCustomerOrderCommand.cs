using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Commands.CancelCustomerOrderCommand;

public sealed record CancelCustomerOrderCommand(string SessionId, CustomerOrderRecoveryDto Request)
    : ICommand<ApiResponse<CustomerOrderResolutionDto>>;

public sealed class CancelCustomerOrderCommandHandler(ICustomerCheckoutAcceptance service)
    : ICommandHandler<CancelCustomerOrderCommand, ApiResponse<CustomerOrderResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderResolutionDto>> Handle(CancelCustomerOrderCommand request,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderResolutionDto>.SuccessWithData(
            await service.CancelAsync(request.SessionId, request.Request, cancellationToken));
}
