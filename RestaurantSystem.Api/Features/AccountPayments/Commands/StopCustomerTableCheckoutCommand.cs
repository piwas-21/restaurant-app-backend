using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record StopCustomerTableCheckoutCommand(string SessionId, CustomerTablePaymentProofDto Proof,
    string? ParticipantToken) : ICommand<ApiResponse<CustomerTableCheckoutDto>>;

public sealed class StopCustomerTableCheckoutCommandHandler(ICustomerTableCheckoutService service)
    : ICommandHandler<StopCustomerTableCheckoutCommand, ApiResponse<CustomerTableCheckoutDto>>
{
    public async Task<ApiResponse<CustomerTableCheckoutDto>> Handle(StopCustomerTableCheckoutCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerTableCheckoutDto>.SuccessWithData(
        await service.RunAsync(command.SessionId, command.Proof, command.ParticipantToken,
            CustomerTableCheckoutAction.Stop, cancellationToken));
}
