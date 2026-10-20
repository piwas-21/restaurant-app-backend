using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeVerificationCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteNativeVerificationCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Proof)
    : ICommand<ApiResponse<string>>;

public sealed class CompleteNativeVerificationCommandHandler(CustomMediator mediator, ICustomerNativeAccountLinks links)
    : ICommandHandler<CompleteNativeVerificationCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(CompleteNativeVerificationCommand command, CancellationToken cancellationToken)
    {
        await links.RequireEnabledAsync(command.SchemaVersion, cancellationToken);
        var proof = links.Read(command.Proof, RestaurantSystem.Api.Features.Auth.Services.CustomerNativeAccountLinks.Verify);
        var email = await links.RequireCurrentEmailAsync(proof, cancellationToken);
        return await mediator.SendCommand(new RestaurantSystem.Api.Features.Auth.Commands.VerifyEmailCommand.VerifyEmailCommand(email, proof.Token)
        { NativeClient = true }, cancellationToken);
    }
}
