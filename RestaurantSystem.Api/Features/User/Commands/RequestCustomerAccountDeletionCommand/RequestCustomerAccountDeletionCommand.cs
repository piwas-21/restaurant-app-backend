using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using SharedDeletionRequest = RestaurantSystem.Api.Features.User.Commands.RequestAccountDeletionCommand.RequestAccountDeletionCommand;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Api.Features.User.Queries.GetCustomerAccountDeletionQuery;
using RestaurantSystem.Api.Features.User.Services;

namespace RestaurantSystem.Api.Features.User.Commands.RequestCustomerAccountDeletionCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestCustomerAccountDeletionCommand([property: JsonRequired] int SchemaVersion)
    : ICommand<ApiResponse<CustomerAccountDeletionDto>>;

public sealed class RequestCustomerAccountDeletionCommandHandler(CustomMediator mediator, ICurrentUserService currentUser)
    : ICommandHandler<RequestCustomerAccountDeletionCommand, ApiResponse<CustomerAccountDeletionDto>>
{
    public async Task<ApiResponse<CustomerAccountDeletionDto>> Handle(RequestCustomerAccountDeletionCommand command,
        CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        // Confirm the current database role/owner before invoking the existing erasure workflow.
        await mediator.SendQuery(new GetCustomerAccountDeletionQuery(), cancellationToken);
        var requested = await mediator.SendCommand(new SharedDeletionRequest(owner, CustomerOnly: true), cancellationToken);
        if (!requested.Success) throw new NotFoundException("Customer account unavailable.");
        return await mediator.SendQuery(new GetCustomerAccountDeletionQuery(), cancellationToken);
    }
}
