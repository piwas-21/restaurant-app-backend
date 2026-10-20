using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.Api.Features.Auth.Commands.RegisterNativeCustomerCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterNativeCustomerCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string FirstName,
    [property: JsonRequired] string LastName, [property: JsonRequired] string Email,
    [property: JsonRequired] string Password, [property: JsonRequired] string ConfirmPassword)
    : ICommand<ApiResponse<string>>;

public sealed class RegisterNativeCustomerCommandHandler(CustomMediator mediator, ICustomerNativeAccountLinks links)
    : ICommandHandler<RegisterNativeCustomerCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(RegisterNativeCustomerCommand command, CancellationToken cancellationToken)
    {
        await links.RequireEnabledAsync(command.SchemaVersion, cancellationToken);
        var result = await mediator.SendCommand(new RestaurantSystem.Api.Features.User.Commands.RegisterCustomerCommand.RegisterCustomerCommand(
            command.FirstName, command.LastName, command.Email, command.Password, command.ConfirmPassword)
        { NativeClient = true }, cancellationToken);
        // Native registration does not mint or publish an authenticated session.
        return result.Success ? ApiResponse<string>.SuccessWithData("Registration completed. Check your email to verify your account.")
            : new ApiResponse<string> { Success = false, Message = result.Message, Errors = result.Errors, ErrorCode = result.ErrorCode };
    }
}
