using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record SubmitCustomerTablePaymentCommand(string SessionId, string? ParticipantToken, CustomerTablePaymentSubmissionDto Request) : ICommand<ApiResponse<CustomerTablePaymentResolutionDto>>;

public sealed class SubmitCustomerTablePaymentCommandHandler(ICustomerTablePaymentOperations service)
    : ICommandHandler<SubmitCustomerTablePaymentCommand, ApiResponse<CustomerTablePaymentResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePaymentResolutionDto>> Handle(SubmitCustomerTablePaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePaymentResolutionDto>.SuccessWithData(await service.SubmitAsync(command.SessionId, command.ParticipantToken, command.Request, cancellationToken));
}
