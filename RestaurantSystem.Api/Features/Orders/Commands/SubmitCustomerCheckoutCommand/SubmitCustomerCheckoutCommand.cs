using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed record SubmitCustomerCheckoutCommand(string SessionId, CustomerCheckoutSubmissionDto Request)
    : ICommand<ApiResponse<CustomerOrderResolutionDto>>;

public sealed class SubmitCustomerCheckoutCommandHandler(ICustomerCheckoutAcceptance service)
    : ICommandHandler<SubmitCustomerCheckoutCommand, ApiResponse<CustomerOrderResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderResolutionDto>> Handle(SubmitCustomerCheckoutCommand request,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderResolutionDto>.SuccessWithData(
            await service.SubmitAsync(request.SessionId, request.Request, cancellationToken));
}
