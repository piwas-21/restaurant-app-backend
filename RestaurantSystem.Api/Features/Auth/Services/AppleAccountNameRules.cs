namespace RestaurantSystem.Api.Features.Auth.Services;

public static class AppleAccountNameRules
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
