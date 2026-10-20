using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Services;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLinkCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteNativeGoogleLinkCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] Guid ChallengeId,
    [property: JsonRequired] string Proof, [property: JsonRequired] string IdToken) : ICommand<ApiResponse<string>>;

public sealed class CompleteNativeGoogleLinkCommandHandler(INativeSocialCompletion completion, ICurrentUserService currentUser)
    : ICommandHandler<CompleteNativeGoogleLinkCommand, ApiResponse<string>>
{
    public async Task<ApiResponse<string>> Handle(CompleteNativeGoogleLinkCommand command, CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        await completion.CompleteAsync(command.ChallengeId, command.Proof, command.IdToken,
            owner, new NativeSocialProfile(null, null), cancellationToken);
        return ApiResponse<string>.SuccessWithData("Provider linked to this restaurant account.");
    }
}
