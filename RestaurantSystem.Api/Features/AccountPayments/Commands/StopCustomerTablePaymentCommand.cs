using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record StopCustomerTablePaymentCommand(string SessionId, CustomerTablePaymentProofDto Proof) : ICommand<ApiResponse<CustomerTablePaymentResolutionDto>>;

public sealed class StopCustomerTablePaymentCommandHandler(ICustomerTablePaymentOperations service)
    : ICommandHandler<StopCustomerTablePaymentCommand, ApiResponse<CustomerTablePaymentResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePaymentResolutionDto>> Handle(StopCustomerTablePaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePaymentResolutionDto>.SuccessWithData(await service.ResolveAsync(command.SessionId, command.Proof, true, cancellationToken));
}
