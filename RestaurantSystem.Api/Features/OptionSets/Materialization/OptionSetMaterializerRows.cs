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

    public static async Task<MaterializedOptionSetRow?> FindAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        OptionSetAppliedRow? mapping,
        CancellationToken cancellationToken)
    {
        if (mapping is not null)
        {
            return await FindMappedAsync(context, role, target, mapping, cancellationToken);
        }

        return role switch
        {
            OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce =>
                await FindIngredientAsync(context, role, target, entry, cancellationToken),
            OptionSetAttachmentRole.SuggestedSide =>
                await FindSideAsync(context, target, entry, cancellationToken),
            OptionSetAttachmentRole.BundleChoice =>
                await FindBundleItemAsync(context, target, entry, cancellationToken),
            OptionSetAttachmentRole.ProductChoice =>
                await FindProductChoiceAsync(context, target, entry, cancellationToken),
            _ => throw new BadRequestException("The option-set role is invalid")
        };
    }

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
                ["displayOrder"] = entryOverride?.DisplayOrder ?? entry.DisplayOrder,
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
                ["displayOrder"] = entryOverride?.DisplayOrder ?? entry.DisplayOrder
            };
        }

        return new Dictionary<string, object?>
        {
            ["additionalPrice"] = entryOverride?.AdditionalPrice ?? entry.AdditionalPrice,
            ["displayOrder"] = entryOverride?.DisplayOrder ?? entry.DisplayOrder,
            ["isDefault"] = entryOverride?.IsDefault ?? entry.IsDefault
        };
    }

    public static Dictionary<string, object?> Current(MaterializedOptionSetRow row) => row.Entity switch
    {
        ProductIngredient ingredient => new()
        {
            ["name"] = ingredient.Name,
            ["displayOrder"] = ingredient.DisplayOrder,
            ["isOptional"] = ingredient.IsOptional,
            ["maxQuantity"] = ingredient.MaxQuantity,
            ["price"] = ingredient.Price,
            ["isIncludedInBasePrice"] = ingredient.IsIncludedInBasePrice,
            ["isActive"] = ingredient.IsActive
        },
        ProductSideItem side => new()
        {
            ["isRequired"] = side.IsRequired,
            ["displayOrder"] = side.DisplayOrder
        },
        MenuSectionItem item => new()
        {
            ["additionalPrice"] = item.AdditionalPrice,
            ["displayOrder"] = item.DisplayOrder,
            ["isDefault"] = item.IsDefault
        },
        ProductCustomizationProductOption option => new()
        {
            ["additionalPrice"] = option.AdditionalPrice,
            ["displayOrder"] = option.DisplayOrder,
            ["isDefault"] = option.IsDefault
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

    private static async Task<MaterializedOptionSetRow?> FindMappedAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        OptionSetAppliedRow mapping,
        CancellationToken cancellationToken)
    {
        object? entity = mapping.RowType switch
        {
            IngredientRow when role is OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce =>
                await context.ProductIngredients.FirstOrDefaultAsync(row => row.Id == mapping.MaterializedRowId && row.ProductId == target.TargetProductId, cancellationToken),
            SideRow when role == OptionSetAttachmentRole.SuggestedSide =>
                await context.ProductSideItems.FirstOrDefaultAsync(row => row.Id == mapping.MaterializedRowId && row.MainProductId == target.TargetProductId, cancellationToken),
            BundleRow when role == OptionSetAttachmentRole.BundleChoice && target.TargetMenuSectionId is Guid sectionId =>
                await context.MenuSectionItems.FirstOrDefaultAsync(row => row.Id == mapping.MaterializedRowId && row.MenuSectionId == sectionId, cancellationToken),
            ProductChoiceRow when role == OptionSetAttachmentRole.ProductChoice && target.TargetCustomizationGroupId is Guid groupId =>
                await context.ProductCustomizationProductOptions.FirstOrDefaultAsync(row => row.Id == mapping.MaterializedRowId && row.ProductCustomizationGroupId == groupId, cancellationToken),
            _ => null
        };
        if (entity is null)
        {
            throw new ConflictException("A mapped option row is missing or no longer belongs to this target");
        }

        return new MaterializedOptionSetRow(mapping.RowType, mapping.MaterializedRowId, entity, mapping.OwnsMaterializedRow);
    }

    private static async Task<MaterializedOptionSetRow?> FindIngredientAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        CancellationToken cancellationToken)
    {
        var rows = await context.ProductIngredients.Where(row => row.ProductId == target.TargetProductId
            && row.GlobalIngredientId == entry.GlobalIngredientId
            && row.Kind == (role == OptionSetAttachmentRole.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient))
            .OrderBy(row => row.DisplayOrder).Take(2).ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => new MaterializedOptionSetRow(IngredientRow, rows[0].Id, rows[0], false),
            _ => throw new ConflictException("This target has duplicate rows for the same canonical ingredient; resolve them before attaching a set")
        };
    }

    private static async Task<MaterializedOptionSetRow?> FindSideAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        CancellationToken cancellationToken)
    {
        var rows = await context.ProductSideItems.Where(row => row.MainProductId == target.TargetProductId
            && row.SideItemProductId == entry.ProductId).OrderBy(row => row.DisplayOrder).Take(2).ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => new MaterializedOptionSetRow(SideRow, rows[0].Id, rows[0], false),
            _ => throw new ConflictException("This target has duplicate suggested-side rows; resolve them before attaching a set")
        };
    }

    private static async Task<MaterializedOptionSetRow?> FindBundleItemAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        CancellationToken cancellationToken)
    {
        var rows = await context.MenuSectionItems.Where(row => row.MenuSectionId == target.TargetMenuSectionId
            && row.ProductId == entry.ProductId && row.ProductVariationId == entry.ProductVariationId)
            .OrderBy(row => row.DisplayOrder).Take(2).ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => new MaterializedOptionSetRow(BundleRow, rows[0].Id, rows[0], false),
            _ => throw new ConflictException("This section has duplicate rows for the same product choice; resolve them before attaching a set")
        };
    }

    private static async Task<MaterializedOptionSetRow?> FindProductChoiceAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.ProductVariationId.HasValue)
        {
            throw new BadRequestException("Product customization groups cannot target a specific product variation");
        }

        var rows = await context.ProductCustomizationProductOptions
            .Where(row => row.ProductCustomizationGroupId == target.TargetCustomizationGroupId
                && row.OptionProductId == entry.ProductId)
            .OrderBy(row => row.DisplayOrder).Take(2).ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => new MaterializedOptionSetRow(ProductChoiceRow, rows[0].Id, rows[0], false),
            _ => throw new ConflictException("This product-choice group has duplicate membership rows; resolve them before attaching a set")
        };
    }

    private static void SetIngredientValue(ProductIngredient row, string key, object? value)
    {
        switch (key)
        {
            case "name": row.Name = (string?)value ?? string.Empty; break;
            case "displayOrder": row.DisplayOrder = (int)value!; break;
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
            case "displayOrder": row.DisplayOrder = (int)value!; break;
        }
    }

    private static void SetBundleValue(MenuSectionItem row, string key, object? value)
    {
        switch (key)
        {
            case "additionalPrice": row.AdditionalPrice = (decimal)value!; break;
            case "displayOrder": row.DisplayOrder = (int)value!; break;
            case "isDefault": row.IsDefault = (bool)value!; break;
        }
    }

    private static void SetProductChoiceValue(ProductCustomizationProductOption row, string key, object? value)
    {
        switch (key)
        {
            case "additionalPrice": row.AdditionalPrice = (decimal)value!; break;
            case "displayOrder": row.DisplayOrder = (int)value!; break;
            case "isDefault": row.IsDefault = (bool)value!; break;
        }
    }
}
