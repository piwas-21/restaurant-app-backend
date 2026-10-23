using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record RecoverCustomerTableCheckoutCommand(string SessionId, CustomerTablePaymentProofDto Proof,
    string? ParticipantToken) : ICommand<ApiResponse<CustomerTableCheckoutDto>>;

public sealed class RecoverCustomerTableCheckoutCommandHandler(ICustomerTableCheckoutService service)
    : ICommandHandler<RecoverCustomerTableCheckoutCommand, ApiResponse<CustomerTableCheckoutDto>>
{
    public async Task<ApiResponse<CustomerTableCheckoutDto>> Handle(RecoverCustomerTableCheckoutCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerTableCheckoutDto>.SuccessWithData(
        await service.RunAsync(command.SessionId, command.Proof, command.ParticipantToken,
            CustomerTableCheckoutAction.Recover, cancellationToken));
}
