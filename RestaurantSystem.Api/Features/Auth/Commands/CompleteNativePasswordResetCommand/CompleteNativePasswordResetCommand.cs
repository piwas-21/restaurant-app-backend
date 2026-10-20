using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativePasswordResetCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteNativePasswordResetCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Proof,
    [property: JsonRequired] string NewPassword, [property: JsonRequired] string ConfirmPassword)
    : ICommand<ApiResponse<string>>;

public sealed class CompleteNativePasswordResetCommandHandler(CustomMediator mediator, ICustomerNativeAccountLinks links)
    : ICommandHandler<CompleteNativePasswordResetCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(CompleteNativePasswordResetCommand command, CancellationToken cancellationToken)
    {
        await links.RequireEnabledAsync(command.SchemaVersion, cancellationToken);
        var proof = links.Read(command.Proof, RestaurantSystem.Api.Features.Auth.Services.CustomerNativeAccountLinks.Recover);
        var email = await links.RequireCurrentEmailAsync(proof, cancellationToken);
        return await mediator.SendCommand(new RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand.ResetPasswordCommand(email, proof.Token,
            command.NewPassword, command.ConfirmPassword)
        { NativeClient = true }, cancellationToken);
    }
}
