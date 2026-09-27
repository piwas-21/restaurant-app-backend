using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerRows
{
    public const string IngredientRow = "ProductIngredient";
    public const string SideRow = "ProductSideItem";
    public const string BundleRow = "MenuSectionItem";
    public const string ProductChoiceRow = "ProductCustomizationProductOption";
    private const string DisplayOrderField = "displayOrder";
    private const string AdditionalPriceField = "additionalPrice";
    private const string IsDefaultField = "isDefault";

    public static Dictionary<string, object?> Desired(
        OptionSetAttachmentRole role,
        OptionSetEntry entry,
        OptionSetEntryOverride? entryOverride)
    {
        if (role is OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = entryOverride?.Name ?? entry.Name,
                [DisplayOrderField] = entryOverride?.DisplayOrder ?? entry.DisplayOrder,
                ["isOptional"] = entryOverride?.IsOptional ?? entry.IsOptional,
                ["maxQuantity"] = entryOverride?.MaxQuantity ?? entry.MaxQuantity,
                ["price"] = entryOverride?.Price ?? entry.Price,
                ["isIncludedInBasePrice"] = entryOverride?.IsIncludedInBasePrice ?? entry.IsIncludedInBasePrice,
                ["isActive"] = true
            };
        }

        if (role == OptionSetAttachmentRole.SuggestedSide)
        {
            return new Dictionary<string, object?>
            {
                ["isRequired"] = entryOverride?.IsRequired ?? entry.IsRequired,
                [DisplayOrderField] = entryOverride?.DisplayOrder ?? entry.DisplayOrder
            };
        }

        return new Dictionary<string, object?>
        {
            [AdditionalPriceField] = entryOverride?.AdditionalPrice ?? entry.AdditionalPrice,
            [DisplayOrderField] = entryOverride?.DisplayOrder ?? entry.DisplayOrder,
            [IsDefaultField] = entryOverride?.IsDefault ?? entry.IsDefault
        };
    }

    public static Dictionary<string, object?> Current(MaterializedOptionSetRow row) => row.Entity switch
    {
        ProductIngredient ingredient => new()
        {
            ["name"] = ingredient.Name,
            [DisplayOrderField] = ingredient.DisplayOrder,
            ["isOptional"] = ingredient.IsOptional,
            ["maxQuantity"] = ingredient.MaxQuantity,
            ["price"] = ingredient.Price,
            ["isIncludedInBasePrice"] = ingredient.IsIncludedInBasePrice,
            ["isActive"] = ingredient.IsActive
        },
        ProductSideItem side => new()
        {
            ["isRequired"] = side.IsRequired,
            [DisplayOrderField] = side.DisplayOrder
        },
        MenuSectionItem item => new()
        {
            [AdditionalPriceField] = item.AdditionalPrice,
            [DisplayOrderField] = item.DisplayOrder,
            [IsDefaultField] = item.IsDefault
        },
        ProductCustomizationProductOption option => new()
        {
            [AdditionalPriceField] = option.AdditionalPrice,
            [DisplayOrderField] = option.DisplayOrder,
            [IsDefaultField] = option.IsDefault
        },
        _ => throw new BadRequestException("The materialized option row is unsupported")
    };

    public static Dictionary<string, JsonElement> DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    public static string SerializeSnapshot(IReadOnlyDictionary<string, object?> values) =>
        JsonSerializer.Serialize(values);

    public static bool SnapshotEquals(JsonElement snapshot, object? current) =>
        snapshot.GetRawText() == JsonSerializer.Serialize(current);

    public static void ApplyValues(MaterializedOptionSetRow row, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var (key, value) in values)
        {
            switch (row.Entity)
            {
                case ProductIngredient ingredient:
                    SetIngredientValue(ingredient, key, value);
                    break;
                case ProductSideItem side:
                    SetSideValue(side, key, value);
                    break;
                case MenuSectionItem item:
                    SetBundleValue(item, key, value);
                    break;
                case ProductCustomizationProductOption option:
                    SetProductChoiceValue(option, key, value);
                    break;
            }
        }
    }

    public static async Task<MaterializedOptionSetRow> CreateAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        OptionSetEntryOverride? entryOverride,
        string audit,
        CancellationToken cancellationToken)
    {
        var desired = Desired(target.Role, entry, entryOverride);
        var now = DateTime.UtcNow;
        if (target.Role is OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce)
        {
            var row = new ProductIngredient
            {
                Id = Guid.NewGuid(),
                ProductId = target.TargetProductId,
                GlobalIngredientId = entry.GlobalIngredientId,
                Kind = kind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient,
                CreatedAt = now,
                CreatedBy = audit
            };
            ApplyValues(new MaterializedOptionSetRow(IngredientRow, row.Id, row, true), desired);
            await context.ProductIngredients.AddAsync(row, cancellationToken);
            return new MaterializedOptionSetRow(IngredientRow, row.Id, row, true);
        }

        if (target.Role == OptionSetAttachmentRole.SuggestedSide)
        {
            var row = new ProductSideItem
            {
                Id = Guid.NewGuid(),
                MainProductId = target.TargetProductId,
                SideItemProductId = entry.ProductId!.Value,
                CreatedAt = now,
                CreatedBy = audit
            };
            ApplyValues(new MaterializedOptionSetRow(SideRow, row.Id, row, true), desired);
            await context.ProductSideItems.AddAsync(row, cancellationToken);
            return new MaterializedOptionSetRow(SideRow, row.Id, row, true);
        }

        if (target.Role == OptionSetAttachmentRole.ProductChoice)
        {
            if (entry.ProductVariationId.HasValue)
            {
                throw new BadRequestException("Product customization groups cannot target a specific product variation");
            }

            var option = new ProductCustomizationProductOption
            {
                Id = Guid.NewGuid(),
                ProductCustomizationGroupId = target.TargetCustomizationGroupId!.Value,
                OptionProductId = entry.ProductId!.Value,
                CreatedAt = now,
                CreatedBy = audit
            };
            ApplyValues(new MaterializedOptionSetRow(ProductChoiceRow, option.Id, option, true), desired);
            await context.ProductCustomizationProductOptions.AddAsync(option, cancellationToken);
            return new MaterializedOptionSetRow(ProductChoiceRow, option.Id, option, true);
        }

        var bundleRow = new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = target.TargetMenuSectionId!.Value,
            ProductId = entry.ProductId!.Value,
            ProductVariationId = entry.ProductVariationId,
            CreatedAt = now,
            CreatedBy = audit
        };
        ApplyValues(new MaterializedOptionSetRow(BundleRow, bundleRow.Id, bundleRow, true), desired);
        await context.MenuSectionItems.AddAsync(bundleRow, cancellationToken);
        return new MaterializedOptionSetRow(BundleRow, bundleRow.Id, bundleRow, true);
    }

    public static void Delete(ApplicationDbContext context, MaterializedOptionSetRow row)
    {
        switch (row.Entity)
        {
            case ProductIngredient ingredient: context.ProductIngredients.Remove(ingredient); break;
            case ProductSideItem side: context.ProductSideItems.Remove(side); break;
            case MenuSectionItem item: context.MenuSectionItems.Remove(item); break;
            case ProductCustomizationProductOption option: context.ProductCustomizationProductOptions.Remove(option); break;
        }
    }

    private static void SetIngredientValue(ProductIngredient row, string key, object? value)
    {
        switch (key)
        {
            case "name": row.Name = (string?)value ?? string.Empty; break;
            case DisplayOrderField: row.DisplayOrder = (int)value!; break;
            case "isOptional": row.IsOptional = (bool)value!; break;
            case "maxQuantity": row.MaxQuantity = (int)value!; break;
            case "price": row.Price = (decimal)value!; break;
            case "isIncludedInBasePrice": row.IsIncludedInBasePrice = (bool)value!; break;
            case "isActive": row.IsActive = (bool)value!; break;
        }
    }

    private static void SetSideValue(ProductSideItem row, string key, object? value)
    {
        switch (key)
        {
            case "isRequired": row.IsRequired = (bool)value!; break;
            case DisplayOrderField: row.DisplayOrder = (int)value!; break;
        }
    }

    private static void SetBundleValue(MenuSectionItem row, string key, object? value)
    {
        switch (key)
        {
            case AdditionalPriceField: row.AdditionalPrice = (decimal)value!; break;
            case DisplayOrderField: row.DisplayOrder = (int)value!; break;
            case IsDefaultField: row.IsDefault = (bool)value!; break;
        }
    }

    private static void SetProductChoiceValue(ProductCustomizationProductOption row, string key, object? value)
    {
        switch (key)
        {
            case AdditionalPriceField: row.AdditionalPrice = (decimal)value!; break;
            case DisplayOrderField: row.DisplayOrder = (int)value!; break;
            case IsDefaultField: row.IsDefault = (bool)value!; break;
        }
    }
}
