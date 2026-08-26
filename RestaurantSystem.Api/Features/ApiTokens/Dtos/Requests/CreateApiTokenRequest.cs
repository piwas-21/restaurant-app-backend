namespace RestaurantSystem.Api.Features.ApiTokens.Dtos.Requests;

/// <summary>Body of <c>POST /api/ApiTokens</c>.</summary>
/// <param name="Name">Human label, 1–100 chars, unique among tokens that are still live.</param>
/// <param name="Scopes">Granted scopes from the <c>ApiTokenScopes</c> vocabulary. Non-empty.</param>
/// <param name="ExpiresInDays">1–365. Required — there is no never-expires token.</param>
public record CreateApiTokenRequest(string Name, List<string> Scopes, int ExpiresInDays);
