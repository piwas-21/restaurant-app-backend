using System.Globalization;

namespace RestaurantSystem.Api.Features.Menus;

public static class MenuAuthoringVersionTag
{
    public static string Format(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    public static bool TryParse(string? value, out int version)
    {
        version = 0;
        if (value is null || value.Length < 3 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        return int.TryParse(
            value.AsSpan(1, value.Length - 2),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out version)
            && version > 0;
    }
}
