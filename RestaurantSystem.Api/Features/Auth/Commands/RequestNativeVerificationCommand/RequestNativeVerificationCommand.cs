using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.Api.Features.Auth.Commands.RequestNativeVerificationCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestNativeVerificationCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Email)
    : ICommand<ApiResponse<string>>;

public sealed class RequestNativeVerificationCommandHandler(CustomMediator mediator, ICustomerNativeAccountLinks links)
    : ICommandHandler<RequestNativeVerificationCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(RequestNativeVerificationCommand command, CancellationToken cancellationToken)
    {
        await links.RequireEnabledAsync(command.SchemaVersion, cancellationToken);
        return await mediator.SendCommand(new RestaurantSystem.Api.Features.Auth.Commands.SendEmailVerificationCommand.SendEmailVerificationCommand(command.Email)
        { NativeClient = true }, cancellationToken);
    }
}
