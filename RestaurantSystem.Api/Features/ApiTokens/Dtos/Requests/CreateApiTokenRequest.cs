using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.ApiTokens.Dtos.Requests;

/// <summary>Body of <c>POST /api/ApiTokens</c>.</summary>
/// <param name="Name">Human label, 1–100 chars, unique among tokens that are still live.</param>
/// <param name="Scopes">Granted scopes from the <c>ApiTokenScopes</c> vocabulary. Non-empty.</param>
/// <param name="ExpiresInDays">
/// 1–365. Required — there is no never-expires token.
/// <para>
/// <c>[JsonRequired]</c> because this is a non-nullable VALUE type: without it a body that omits
/// the field binds to <c>0</c>, which is indistinguishable from a caller who sent zero. That is
/// the under-posting Sonar S6964 is about, and here it would have been silent — a client that
/// forgot the field would be told "expiry must be between 1 and 365", naming a value it never
/// sent. With the attribute the omission is refused as an omission.
/// </para>
/// </param>
public record CreateApiTokenRequest(
    string Name,
    List<string> Scopes,
    [property: JsonRequired] int ExpiresInDays);
