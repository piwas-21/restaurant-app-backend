using RestaurantSystem.Api.Common.Services;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>
/// Resolves only this provider's product keys. URI normalization is not a filesystem boundary:
/// decode each segment once, reject separators, and check the rooted path and symlink components.
/// The configured content root is trusted; uploads cannot create links through the HTTP API.
/// </summary>
internal sealed class ProductCardVariantPaths(string uploadsRoot, Uri baseUri)
{
    private readonly string _root = Path.GetFullPath(uploadsRoot);
    private readonly string _prefix = baseUri.AbsolutePath.TrimEnd('/') + "/";

    public CardVariantLocation Resolve(string url, Guid productId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != baseUri.Scheme || uri.Authority != baseUri.Authority
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith(_prefix, StringComparison.Ordinal))
        {
            throw new IOException("Image URL is outside the configured local storage namespace.");
        }

        // Uri normalises literal and encoded dot-segments away before AbsolutePath, so the guard
        // runs on the RAW stored string: a URL carrying "..", encoded dots or encoded separators
        // is corrupt data even when its normalized target happens to be safe. Stored product
        // keys are generated names and never contain any of these.
        if (url.Contains("..", StringComparison.Ordinal)
            || url.Contains("%2e", StringComparison.OrdinalIgnoreCase)
            || url.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || url.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            || url.Contains("%00", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Image URL contains dot or encoded separator segments.");
        }

        var segments = uri.AbsolutePath[_prefix.Length..].Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length != 3 || segments[0] != "products"
            || !Guid.TryParse(segments[1], out var folderId) || folderId != productId
            || segments.Any(IsUnsafeSegment))
        {
            throw new IOException("Image URL does not identify a safe product image key.");
        }

        var original = Path.Combine(_root, Path.Combine(segments));
        var variantName = ProductImageCardVariants.VariantFileName(segments[2]);
        var variant = Path.Combine(_root, segments[0], segments[1], variantName);
        EnsureSafe(original);
        EnsureSafe(variant);
        var relative = string.Join('/', segments.Take(2).Append(variantName).Select(Uri.EscapeDataString));
        var cardUrl = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/" + relative).AbsoluteUri;
        return new CardVariantLocation(original, variant, segments[2], cardUrl);
    }

    public void EnsureSafe(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("Image path escapes the uploads root.");
        }

        RejectLink(_root);
        var current = _root;
        foreach (var segment in Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
    }

    private static bool IsUnsafeSegment(string value) =>
        string.IsNullOrEmpty(value) || value is "." or ".."
        || value.IndexOfAny(['/', '\\', '%', ':', '\0']) >= 0
        || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                throw new IOException("Image path contains a symbolic link.");
            }
        }
        catch (FileNotFoundException)
        {
            // An absent original is accounted for by the caller; absent targets are expected.
        }
        catch (DirectoryNotFoundException)
        {
            // The key may be valid even when its original directory no longer exists.
        }
    }
}

internal sealed record CardVariantLocation(string OriginalPath, string VariantPath, string FileName, string CardUrl);
