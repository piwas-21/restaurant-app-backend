using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Api.Features.User.Services;
using SharedCommand = RestaurantSystem.Api.Features.Addresses.Commands.CreateAddressCommand.CreateAddressCommand;
namespace RestaurantSystem.Api.Features.Addresses.Commands.CreateCustomerAddressCommand;

public sealed record CreateCustomerAddressCommand(CustomerAddressWriteRequest Request) : ICommand<ApiResponse<CustomerAddressWriteResultDto>>;
public sealed class CreateCustomerAddressCommandHandler(CustomMediator mediator, ICurrentUserService currentUser)
    : ICommandHandler<CreateCustomerAddressCommand, ApiResponse<CustomerAddressWriteResultDto>>
{
    public async Task<ApiResponse<CustomerAddressWriteResultDto>> Handle(CreateCustomerAddressCommand command, CancellationToken cancellationToken)
    {
        CustomerAccountAccess.RequireOwner(currentUser);
        var result = await mediator.SendCommand(CustomerAddressCommands.Copy(command.Request.Address, new SharedCommand { RequireCustomer = true }), cancellationToken);
        return CustomerAddressCommands.Written(result);
    }
}
