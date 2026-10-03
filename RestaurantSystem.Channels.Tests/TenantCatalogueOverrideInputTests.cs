using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantCatalogueOverrideInputTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void GatewayOverrideRequiresSelectedInsteadOfDefaultingToDeselection()
    {
        const string identity = "\"productId\":\"11111111-1111-1111-1111-111111111111\",\"variationId\":null,\"categoryId\":\"22222222-2222-2222-2222-222222222222\"";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantManagementItemOverride>(
            $"{{{identity}}}", Options));
        var explicitFalse = JsonSerializer.Deserialize<TenantManagementItemOverride>(
            $"{{{identity},\"selected\":false}}", Options);

        Assert.NotNull(explicitFalse);
        Assert.False(explicitFalse.Selected);
    }

    [Fact]
    public void CategoryItemReferenceRequiresExplicitNullableVariationAndCategoryFields()
    {
        const string product = "\"productId\":\"11111111-1111-1111-1111-111111111111\"";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantCatalogueItemReference>(
            $"{{{product},\"categoryId\":null}}", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantCatalogueItemReference>(
            $"{{{product},\"variationId\":null}}", Options));
        var explicitBase = JsonSerializer.Deserialize<TenantCatalogueItemReference>(
            $"{{{product},\"variationId\":null,\"categoryId\":\"22222222-2222-2222-2222-222222222222\"}}", Options);

        Assert.NotNull(explicitBase);
        Assert.Null(explicitBase.VariationId);
    }
}
