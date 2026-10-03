namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentTillReferencePolicy
{
    internal static bool IsValid(string? reference) => reference is { Length: > 0 and <= 80 }
        && reference.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or '/' or '#');
}
