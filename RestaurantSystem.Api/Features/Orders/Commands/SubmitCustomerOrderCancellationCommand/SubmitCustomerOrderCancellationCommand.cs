using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

public sealed record SubmitCustomerOrderCancellationCommand(CustomerOrderCancellationIntentDto Intent) : ICommand<ApiResponse<CustomerOrderCancellationResolutionDto>>;

public sealed class SubmitCustomerOrderCancellationCommandHandler(ICustomerOrderCancellationService service)
    : ICommandHandler<SubmitCustomerOrderCancellationCommand, ApiResponse<CustomerOrderCancellationResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationResolutionDto>> Handle(SubmitCustomerOrderCancellationCommand request, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationResolutionDto>.SuccessWithData(await service.SubmitCustomerAsync(request.Intent, cancellationToken));
}
