using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>
/// One GET of Apple's JWKS document, over the typed <c>HttpClient</c> registered in
/// <c>AppleAuthExtensions</c> (which carries the timeout). Deliberately dumb: no caching,
/// no retry, no key selection — see <see cref="AppleSigningKeyProvider"/> for those.
/// </summary>
public sealed class AppleJwksFetcher : IAppleJwksFetcher
{
    private readonly HttpClient _httpClient;
    private readonly AppleAuthSettings _settings;

    public AppleJwksFetcher(HttpClient httpClient, IOptions<AppleAuthSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<JsonWebKeySet> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(_settings.JwksUri), cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonWebKeySet.Create(json);
    }
}
