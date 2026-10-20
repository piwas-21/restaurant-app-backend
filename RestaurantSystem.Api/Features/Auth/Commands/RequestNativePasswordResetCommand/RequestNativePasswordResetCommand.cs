using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.Api.Features.Auth.Commands.RequestNativePasswordResetCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestNativePasswordResetCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Email)
    : ICommand<ApiResponse<string>>;

public sealed class RequestNativePasswordResetCommandHandler(CustomMediator mediator, ICustomerNativeAccountLinks links)
    : ICommandHandler<RequestNativePasswordResetCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(RequestNativePasswordResetCommand command, CancellationToken cancellationToken)
    {
        await links.RequireEnabledAsync(command.SchemaVersion, cancellationToken);
        return await mediator.SendCommand(new RestaurantSystem.Api.Features.Auth.Commands.ForgotPasswordCommand.ForgotPasswordCommand(command.Email)
        { NativeClient = true }, cancellationToken);
    }
}
