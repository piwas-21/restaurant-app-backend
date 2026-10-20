using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLoginCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteNativeGoogleLoginCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] Guid ChallengeId,
    [property: JsonRequired] string Proof, [property: JsonRequired] string IdToken,
    string? FirstName, string? LastName) : ICommand<ApiResponse<AuthResponse>>;

public sealed class CompleteNativeGoogleLoginCommandHandler(INativeSocialCompletion completion, LoginEventHandler login, IHttpContextAccessor http)
    : ICommandHandler<CompleteNativeGoogleLoginCommand, ApiResponse<AuthResponse>>
{
    public async Task<ApiResponse<AuthResponse>> Handle(CompleteNativeGoogleLoginCommand command, CancellationToken cancellationToken)
    {
        var response = await completion.CompleteAsync(command.ChallengeId, command.Proof, command.IdToken,
            null, new NativeSocialProfile(command.FirstName, command.LastName), cancellationToken)
            ?? throw new BadRequestException("Provider sign-in could not be verified.");
        // Login/session transaction is complete before optional existing basket reconciliation.
        var session = http.HttpContext?.Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(session)) await login.HandleUserLogin(response.UserId, session);
        return ApiResponse<AuthResponse>.SuccessWithData(response);
    }
}
