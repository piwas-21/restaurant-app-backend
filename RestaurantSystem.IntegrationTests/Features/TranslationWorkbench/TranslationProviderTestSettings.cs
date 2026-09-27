using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.TranslationWorkbench;

internal static class TranslationProviderTestSettings
{
    // The HTTP handler is replaced in these tests, so this HTTPS loopback endpoint cannot call a provider.
    public static string Endpoint => new UriBuilder(Uri.UriSchemeHttps, IPAddress.Loopback.ToString())
    {
        Path = "/v1/responses"
    }.Uri.AbsoluteUri;

    public static string GeminiBaseUrl => new UriBuilder(Uri.UriSchemeHttps, IPAddress.Loopback.ToString())
    {
        Path = "/v1beta"
    }.Uri.AbsoluteUri;
}
