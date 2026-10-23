namespace RestaurantSystem.Api.Features.Payments.Services;

public static class TrustedHostedCheckoutUrl
{
    private const int MaximumUrlLength = 8192;
    public static bool IsAllowed(string? url, IReadOnlyCollection<string> origins) =>
        url is { Length: > 0 and <= MaximumUrlLength } && !url.Any(char.IsControl) && !url.Contains('\\')
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
}
