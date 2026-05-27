namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// Joins URL segments while collapsing redundant separators.
/// Purpose-built for stitching together an S3 (or any other) base URL with
/// a stored key/path so neither side has to be careful about leading or
/// trailing slashes. This is intentionally NOT a general URL parser — it
/// only normalises the separator between two segments.
/// </summary>
public static class UrlJoin
{
    /// <summary>
    /// Joins <paramref name="baseUrl"/> and <paramref name="path"/> with
    /// exactly one '/' between them.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    ///   <item>Trailing '/' on <paramref name="baseUrl"/> is trimmed.</item>
    ///   <item>Leading '/' on <paramref name="path"/> is trimmed.</item>
    ///   <item>If <paramref name="path"/> is null or empty, <see cref="string.Empty"/> is returned —
    ///         the bucket root is never a valid image URL, so callers can rely on
    ///         <c>?? fallback</c> chains to kick in.</item>
    ///   <item>If <paramref name="baseUrl"/> is null or empty, the trimmed path is returned.</item>
    /// </list>
    /// Query strings and fragments are preserved as-is — no slash is inserted before '?' or '#'
    /// because the leading-slash trim only touches the very first character.
    /// </remarks>
    public static string Join(string? baseUrl, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        // If path is already an absolute http(s) URL (e.g. a CDN-hosted image
        // stored directly in the DB), return it as-is — prepending baseUrl
        // would corrupt it. Scheme-restricted so leading-slash relative paths
        // (which Uri.TryCreate parses as file:// on Unix) still hit the
        // normal trim+join branch below. PR #67 review.
        if (Uri.TryCreate(path, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            return path;
        }

        if (string.IsNullOrEmpty(baseUrl))
        {
            return path.TrimStart('/');
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }
}
