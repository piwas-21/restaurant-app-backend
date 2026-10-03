namespace RestaurantSystem.Channels.Api;

internal static class TenantCatalogueSelectionIds
{
    public static string SelectionKey(Guid productId, Guid? variationId)
        => $"{productId:D}:{variationId?.ToString("D") ?? "base"}";

    public static string Item(Guid productId, Guid? variationId)
        => $"sofra-item-{productId:N}{(variationId is { } id ? $"-{id:N}" : string.Empty)}";

    public static string Category(Guid categoryId) => $"sofra-category-{categoryId:N}";
}
