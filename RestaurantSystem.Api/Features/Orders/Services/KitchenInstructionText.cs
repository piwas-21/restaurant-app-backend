using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Removes legacy staff-generated summaries while retaining genuine customer comments.</summary>
internal static class KitchenInstructionText
{
    internal static string? For(OrderItem item)
    {
        var instructions = item.SpecialInstructions?.Trim();
        if (string.IsNullOrWhiteSpace(instructions)) return null;
        var parts = instructions.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var ingredients = item.IngredientSnapshots;
        var added = ingredients.Where(value => !value.IsRemoved && value.Quantity > 0
            && (value.IsAddOn || value.Quantity > 1)).Select(value => value.Quantity > 1
                ? $"{value.Quantity}× {value.IngredientName}" : value.IngredientName).ToArray();
        var removed = ingredients.Where(value => value.IsRemoved).Select(value => value.IngredientName).ToArray();
        var sides = item.ChildOrderItems.Select(value => value.ProductName).ToArray();
        bool Generated(string part) => Repeats(part, "Add: ", added)
            || Repeats(part, "No: ", removed) || Repeats(part, "Sides: ", sides);
        var generatedSummary = parts.Any(Generated);
        var remaining = parts.Where(part => !Generated(part) &&
            !(Same(part, item.VariationName) && (generatedSummary || parts.Length == 1))).ToArray();
        if (parts.Length > 0 && remaining.Length == parts.Length)
            return item.SpecialInstructions;
        var comment = string.Join(" | ", remaining);
        return string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    private static bool Repeats(string part, string prefix, string[] expected) =>
        expected.Length > 0 && part.StartsWith(prefix, StringComparison.Ordinal)
        && part[prefix.Length..].Split(',').Select(Normalize).Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(expected.SelectMany(value => value.Split(',')).Select(Normalize)
                .Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool Same(string left, string? right) => right is not null && string.Equals(
        Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
