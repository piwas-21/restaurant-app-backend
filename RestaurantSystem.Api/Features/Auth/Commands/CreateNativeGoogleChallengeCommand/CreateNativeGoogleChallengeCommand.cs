using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.NativeSocial;

namespace RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleChallengeCommand;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateNativeGoogleChallengeCommand([property: JsonRequired] int SchemaVersion) : ICommand<ApiResponse<NativeSocialChallengeDto>>;

public sealed class CreateNativeGoogleChallengeCommandHandler(INativeSocialChallenges challenges)
    : ICommandHandler<CreateNativeGoogleChallengeCommand, ApiResponse<NativeSocialChallengeDto>>
{
    public async Task<ApiResponse<NativeSocialChallengeDto>> Handle(CreateNativeGoogleChallengeCommand command, CancellationToken cancellationToken)
    {
        var challenge = await challenges.CreateAsync("google", null, null, cancellationToken);
        return ApiResponse<NativeSocialChallengeDto>.SuccessWithData(challenge);
    }
}
