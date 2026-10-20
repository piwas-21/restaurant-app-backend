using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitOriginalOrderCancellationCommand;

public sealed record SubmitOriginalOrderCancellationCommand(string SessionId, CustomerOrderCancellationGuestDto Request) : ICommand<ApiResponse<CustomerOrderCancellationResolutionDto>>;

public sealed class SubmitOriginalOrderCancellationCommandHandler(ICustomerOrderCancellationService service)
    : ICommandHandler<SubmitOriginalOrderCancellationCommand, ApiResponse<CustomerOrderCancellationResolutionDto>>
{
    public async Task<ApiResponse<CustomerOrderCancellationResolutionDto>> Handle(SubmitOriginalOrderCancellationCommand request, CancellationToken cancellationToken) =>
        ApiResponse<CustomerOrderCancellationResolutionDto>.SuccessWithData(await service.SubmitOriginalAsync(request.SessionId, request.Request, cancellationToken));
}
