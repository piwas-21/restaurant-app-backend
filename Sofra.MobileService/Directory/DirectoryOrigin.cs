namespace Sofra.MobileService.Directory;

public static class DirectoryOrigin
{
    public static bool IsCanonical(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        return string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)
            && string.Equals(uri.Host, uri.IdnHost, StringComparison.Ordinal);
    }
}
