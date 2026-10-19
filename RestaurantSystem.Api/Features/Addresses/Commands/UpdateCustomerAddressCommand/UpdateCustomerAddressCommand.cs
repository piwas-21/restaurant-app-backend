using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Api.Features.User.Services;
using SharedCommand = RestaurantSystem.Api.Features.Addresses.Commands.UpdateAddressCommand.UpdateAddressCommand;
namespace RestaurantSystem.Api.Features.Addresses.Commands.UpdateCustomerAddressCommand;

public sealed record UpdateCustomerAddressCommand(Guid Id, CustomerAddressWriteRequest Request) : ICommand<ApiResponse<CustomerAddressWriteResultDto>>;
public sealed class UpdateCustomerAddressCommandHandler(CustomMediator mediator, ICurrentUserService currentUser)
    : ICommandHandler<UpdateCustomerAddressCommand, ApiResponse<CustomerAddressWriteResultDto>>
{
    public async Task<ApiResponse<CustomerAddressWriteResultDto>> Handle(UpdateCustomerAddressCommand command, CancellationToken cancellationToken)
    {
        CustomerAccountAccess.RequireOwner(currentUser);
        var result = await mediator.SendCommand(CustomerAddressCommands.Copy(command.Request.Address, new SharedCommand { Id = command.Id, RequireCustomer = true, PreserveCoordinates = true }), cancellationToken);
        return CustomerAddressCommands.Written(result);
    }
}
