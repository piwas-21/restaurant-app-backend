using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record StartCustomerTableCheckoutCommand(string SessionId, CustomerTablePaymentProofDto Proof,
    string? ParticipantToken) : ICommand<ApiResponse<CustomerTableCheckoutDto>>;

public sealed class StartCustomerTableCheckoutCommandHandler(ICustomerTableCheckoutService service)
    : ICommandHandler<StartCustomerTableCheckoutCommand, ApiResponse<CustomerTableCheckoutDto>>
{
    public async Task<ApiResponse<CustomerTableCheckoutDto>> Handle(StartCustomerTableCheckoutCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerTableCheckoutDto>.SuccessWithData(
        await service.RunAsync(command.SessionId, command.Proof, command.ParticipantToken,
            CustomerTableCheckoutAction.Start, cancellationToken));
}
