using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Api.Features.User.Services;
using SharedCommand = RestaurantSystem.Api.Features.Addresses.Commands.DeleteAddressCommand.DeleteAddressCommand;
namespace RestaurantSystem.Api.Features.Addresses.Commands.RemoveCustomerAddressCommand;

public sealed record RemoveCustomerAddressCommand(Guid Id, CustomerAddressActionRequest Request) : ICommand<ApiResponse<CustomerAddressActionResultDto>>;
public sealed class RemoveCustomerAddressCommandHandler(CustomMediator mediator, ICurrentUserService currentUser)
    : ICommandHandler<RemoveCustomerAddressCommand, ApiResponse<CustomerAddressActionResultDto>>
{
    public async Task<ApiResponse<CustomerAddressActionResultDto>> Handle(RemoveCustomerAddressCommand command, CancellationToken cancellationToken)
    {
        CustomerAccountAccess.RequireOwner(currentUser);
        var result = await mediator.SendCommand(new SharedCommand(command.Id) { RequireCustomer = true }, cancellationToken);
        return CustomerAddressCommands.Applied(result, command.Id, "removed");
    }
}
