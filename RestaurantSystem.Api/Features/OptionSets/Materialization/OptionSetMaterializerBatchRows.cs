using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerBatchRows
{
    private const string IngredientRow = OptionSetMaterializerRows.IngredientRow;
    private const string SideRow = OptionSetMaterializerRows.SideRow;
    private const string BundleRow = OptionSetMaterializerRows.BundleRow;
    private const string ProductChoiceRow = OptionSetMaterializerRows.ProductChoiceRow;

    public static async Task<IReadOnlyDictionary<Guid, MaterializedOptionSetRowLookup>> FindManyAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        IReadOnlyDictionary<Guid, OptionSetAppliedRow> mappings,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, MaterializedOptionSetRowLookup>();
        var expectedRowType = RowTypeFor(role);
        var mappedRows = await LoadMappedRowsAsync(context, role, target,
            mappings.Values.Where(mapping => mapping.RowType == expectedRowType)
                .Select(mapping => mapping.MaterializedRowId).Distinct().ToArray(), cancellationToken);
        var mappedById = mappedRows.ToDictionary(row => row.Id);
        foreach (var mapping in mappings.Values)
        {
            if (mapping.RowType != expectedRowType || !mappedById.TryGetValue(mapping.MaterializedRowId, out var row))
            {
                result[mapping.OptionSetEntryId] = new MaterializedOptionSetRowLookup(
                    null, "A mapped option row is missing or no longer belongs to this target");
                continue;
            }

            result[mapping.OptionSetEntryId] = new MaterializedOptionSetRowLookup(
                row with { OwnsRow = mapping.OwnsMaterializedRow }, null);
        }

        var unmappedEntries = entries.Where(entry => !mappings.ContainsKey(entry.Id)).ToList();
        var candidateRows = await LoadCanonicalRowsAsync(context, role, target, unmappedEntries, cancellationToken);
        foreach (var entry in unmappedEntries)
        {
            var matches = candidateRows.Where(row => MatchesCanonical(role, entry, row)).Take(2).ToList();
            if (matches.Count > 1)
            {
                result[entry.Id] = new MaterializedOptionSetRowLookup(
                    null, DuplicateRowMessage(role));
            }
            else
            {
                result[entry.Id] = new MaterializedOptionSetRowLookup(matches.SingleOrDefault(), null);
            }
        }

        return result;
    }

    public static async Task<HashSet<Guid>> LoadSharedOwnedRowIdsAsync(
        ApplicationDbContext context,
        IEnumerable<OptionSetAppliedRow> mappings,
        CancellationToken cancellationToken)
    {
        var ownedRows = mappings.Where(mapping => mapping.OwnsMaterializedRow)
            .Select(mapping => mapping.MaterializedRowId).Distinct().ToArray();
        if (ownedRows.Length == 0)
        {
            return [];
        }

        var mappingIds = mappings.Select(mapping => mapping.Id).ToArray();
        var shared = await context.OptionSetAppliedRows.Where(other => !mappingIds.Contains(other.Id)
                && ownedRows.Contains(other.MaterializedRowId))
            .Select(other => other.MaterializedRowId).Distinct().ToListAsync(cancellationToken);
        return shared.ToHashSet();
    }

    private static string RowTypeFor(OptionSetAttachmentRole role) => role switch
    {
        OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce => IngredientRow,
        OptionSetAttachmentRole.SuggestedSide => SideRow,
        OptionSetAttachmentRole.BundleChoice => BundleRow,
        OptionSetAttachmentRole.ProductChoice => ProductChoiceRow,
        _ => throw new BadRequestException("The option-set role is invalid")
    };

    private static async Task<List<MaterializedOptionSetRow>> LoadMappedRowsAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        return role switch
        {
            OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce =>
                (await context.ProductIngredients.Where(row => row.ProductId == target.TargetProductId
                    && ids.Contains(row.Id)).ToListAsync(cancellationToken))
                .Select(row => new MaterializedOptionSetRow(IngredientRow, row.Id, row, false)).ToList(),
            OptionSetAttachmentRole.SuggestedSide =>
                (await context.ProductSideItems.Where(row => row.MainProductId == target.TargetProductId
                    && ids.Contains(row.Id)).ToListAsync(cancellationToken))
                .Select(row => new MaterializedOptionSetRow(SideRow, row.Id, row, false)).ToList(),
            OptionSetAttachmentRole.BundleChoice when target.TargetMenuSectionId is Guid sectionId =>
                (await context.MenuSectionItems.Where(row => row.MenuSectionId == sectionId
                    && ids.Contains(row.Id)).ToListAsync(cancellationToken))
                .Select(row => new MaterializedOptionSetRow(BundleRow, row.Id, row, false)).ToList(),
            OptionSetAttachmentRole.ProductChoice when target.TargetCustomizationGroupId is Guid groupId =>
                (await context.ProductCustomizationProductOptions.Where(row => row.ProductCustomizationGroupId == groupId
                    && ids.Contains(row.Id)).ToListAsync(cancellationToken))
                .Select(row => new MaterializedOptionSetRow(ProductChoiceRow, row.Id, row, false)).ToList(),
            _ => []
        };
    }

    private static async Task<List<MaterializedOptionSetRow>> LoadCanonicalRowsAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        return role switch
        {
            OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce =>
                await LoadCanonicalIngredientRowsAsync(context, role, target, entries, cancellationToken),
            OptionSetAttachmentRole.SuggestedSide =>
                await LoadCanonicalSideRowsAsync(context, target, entries, cancellationToken),
            OptionSetAttachmentRole.BundleChoice =>
                await LoadCanonicalBundleRowsAsync(context, target, entries, cancellationToken),
            OptionSetAttachmentRole.ProductChoice =>
                await LoadCanonicalProductChoiceRowsAsync(context, target, entries, cancellationToken),
            _ => throw new BadRequestException("The option-set role is invalid")
        };
    }

    private static async Task<List<MaterializedOptionSetRow>> LoadCanonicalIngredientRowsAsync(
        ApplicationDbContext context,
        OptionSetAttachmentRole role,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        CancellationToken cancellationToken)
    {
        var ids = entries.Where(entry => entry.GlobalIngredientId.HasValue)
            .Select(entry => entry.GlobalIngredientId!.Value).Distinct().ToArray();
        var kind = role == OptionSetAttachmentRole.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
        return (await context.ProductIngredients.Where(row => row.ProductId == target.TargetProductId
                && row.Kind == kind && row.GlobalIngredientId.HasValue && ids.Contains(row.GlobalIngredientId.Value))
            .ToListAsync(cancellationToken))
            .Select(row => new MaterializedOptionSetRow(IngredientRow, row.Id, row, false)).ToList();
    }

    private static async Task<List<MaterializedOptionSetRow>> LoadCanonicalSideRowsAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        CancellationToken cancellationToken)
    {
        var ids = entries.Where(entry => entry.ProductId.HasValue)
            .Select(entry => entry.ProductId!.Value).Distinct().ToArray();
        return (await context.ProductSideItems.Where(row => row.MainProductId == target.TargetProductId
                && ids.Contains(row.SideItemProductId)).ToListAsync(cancellationToken))
            .Select(row => new MaterializedOptionSetRow(SideRow, row.Id, row, false)).ToList();
    }

    private static async Task<List<MaterializedOptionSetRow>> LoadCanonicalBundleRowsAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        CancellationToken cancellationToken)
    {
        if (target.TargetMenuSectionId is not Guid sectionId)
        {
            return [];
        }

        var productIds = entries.Where(entry => entry.ProductId.HasValue)
            .Select(entry => entry.ProductId!.Value).Distinct().ToArray();
        return (await context.MenuSectionItems.Where(row => row.MenuSectionId == sectionId
                && productIds.Contains(row.ProductId)).ToListAsync(cancellationToken))
            .Select(row => new MaterializedOptionSetRow(BundleRow, row.Id, row, false)).ToList();
    }

    private static async Task<List<MaterializedOptionSetRow>> LoadCanonicalProductChoiceRowsAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        List<OptionSetEntry> entries,
        CancellationToken cancellationToken)
    {
        if (target.TargetCustomizationGroupId is not Guid groupId)
        {
            return [];
        }

        var productIds = entries.Where(entry => entry.ProductId.HasValue)
            .Select(entry => entry.ProductId!.Value).Distinct().ToArray();
        return (await context.ProductCustomizationProductOptions.Where(row => row.ProductCustomizationGroupId == groupId
                && productIds.Contains(row.OptionProductId)).ToListAsync(cancellationToken))
            .Select(row => new MaterializedOptionSetRow(ProductChoiceRow, row.Id, row, false)).ToList();
    }

    private static bool MatchesCanonical(
        OptionSetAttachmentRole role,
        OptionSetEntry entry,
        MaterializedOptionSetRow row) => row.Entity switch
        {
            ProductIngredient ingredient => role is OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce
                && ingredient.GlobalIngredientId == entry.GlobalIngredientId,
            ProductSideItem side => role == OptionSetAttachmentRole.SuggestedSide
                && side.SideItemProductId == entry.ProductId,
            MenuSectionItem item => role == OptionSetAttachmentRole.BundleChoice
                && item.ProductId == entry.ProductId && item.ProductVariationId == entry.ProductVariationId,
            ProductCustomizationProductOption option => role == OptionSetAttachmentRole.ProductChoice
                && option.OptionProductId == entry.ProductId,
            _ => false
        };

    private static string DuplicateRowMessage(OptionSetAttachmentRole role) => role switch
    {
        OptionSetAttachmentRole.Ingredient or OptionSetAttachmentRole.Sauce =>
            "This target has duplicate rows for the same canonical ingredient; resolve them before attaching a set",
        OptionSetAttachmentRole.SuggestedSide =>
            "This target has duplicate suggested-side rows; resolve them before attaching a set",
        OptionSetAttachmentRole.BundleChoice =>
            "This section has duplicate rows for the same product choice; resolve them before attaching a set",
        OptionSetAttachmentRole.ProductChoice =>
            "This product-choice group has duplicate membership rows; resolve them before attaching a set",
        _ => "This target has duplicate option rows; resolve them before attaching a set"
    };

}

internal sealed record MaterializedOptionSetRowLookup(MaterializedOptionSetRow? Row, string? ConflictMessage);
