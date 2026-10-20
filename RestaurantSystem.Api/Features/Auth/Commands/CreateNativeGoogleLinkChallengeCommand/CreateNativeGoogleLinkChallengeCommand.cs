using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Services;

namespace RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleLinkChallengeCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateNativeGoogleLinkChallengeCommand([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string CurrentPassword) : ICommand<ApiResponse<NativeSocialChallengeDto>>;

public sealed class CreateNativeGoogleLinkChallengeCommandHandler(INativeSocialChallenges challenges, ICurrentUserService currentUser)
    : ICommandHandler<CreateNativeGoogleLinkChallengeCommand, ApiResponse<NativeSocialChallengeDto>>
{
    public async Task<ApiResponse<NativeSocialChallengeDto>> Handle(CreateNativeGoogleLinkChallengeCommand command, CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        var challenge = await challenges.CreateAsync("google", owner, command.CurrentPassword, cancellationToken);
        return ApiResponse<NativeSocialChallengeDto>.SuccessWithData(challenge);
    }
}
