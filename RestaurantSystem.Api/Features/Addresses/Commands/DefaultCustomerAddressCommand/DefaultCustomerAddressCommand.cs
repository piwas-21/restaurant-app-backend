using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Api.Features.User.Services;
using SharedCommand = RestaurantSystem.Api.Features.Addresses.Commands.SetDefaultAddressCommand.SetDefaultAddressCommand;
namespace RestaurantSystem.Api.Features.Addresses.Commands.DefaultCustomerAddressCommand;

public sealed record DefaultCustomerAddressCommand(Guid Id, CustomerAddressActionRequest Request) : ICommand<ApiResponse<CustomerAddressActionResultDto>>;
public sealed class DefaultCustomerAddressCommandHandler(CustomMediator mediator, ICurrentUserService currentUser)
    : ICommandHandler<DefaultCustomerAddressCommand, ApiResponse<CustomerAddressActionResultDto>>
{
    public async Task<ApiResponse<CustomerAddressActionResultDto>> Handle(DefaultCustomerAddressCommand command, CancellationToken cancellationToken)
    {
        CustomerAccountAccess.RequireOwner(currentUser);
        var result = await mediator.SendCommand(new SharedCommand(command.Id) { RequireCustomer = true }, cancellationToken);
        return CustomerAddressCommands.Applied(result, command.Id, "default");
    }
}
