namespace RestaurantSystem.Domain.Common;

/// <summary>Small, provider-independent helper for optional ISO-4217 alpha-3 values.</summary>
public static class CurrencyCode
{
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    public static bool IsValid(string? value)
    {
        var normalized = value?.Trim();
        return normalized is { Length: 3 } && normalized.All(char.IsAsciiLetter);
    }
}
