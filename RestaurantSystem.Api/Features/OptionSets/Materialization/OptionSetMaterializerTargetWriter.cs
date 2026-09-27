using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerTargetWriter
{
    public static async Task<OptionSetMaterializationTargetResultDto> ApplyAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        string idempotencyKey,
        string audit,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var state = await OptionSetMaterializerTargetLoader.LoadAsync(
            context, set, target, idempotencyKey, stagedProductIds, cancellationToken);
        var result = new OptionSetMaterializationTargetResultDto
        {
            TargetKey = target.TargetKey,
            AttachmentId = state.Attachment?.Id,
            AttachmentVersion = state.Attachment?.Version
        };
        if (state.Attachment?.LastIdempotencyKey == idempotencyKey
            && state.Attachment.AppliedSetVersion == set.Version)
        {
            result.Status = "unchanged";
            result.AppliedRows = state.Attachment.AppliedRows.Select(row => new OptionSetMaterializationAppliedRowDto
            {
                EntryId = row.OptionSetEntryId,
                RowType = row.RowType,
                RowId = row.MaterializedRowId,
                Action = "unchanged"
            }).ToList();
            result.MenuAuthoringVersion = state.MenuDefinition?.AuthoringVersion;
            result.CustomizationGroupVersion = state.CustomizationGroup?.AuthoringVersion;
            return result;
        }

        var now = DateTime.UtcNow;
        var attachment = state.Attachment ?? NewAttachment(set, target, state, audit, now);
        if (state.Attachment is null)
        {
            await context.OptionSetAttachments.AddAsync(attachment, cancellationToken);
        }

        var desiredIds = state.SelectedEntries.Select(entry => entry.Id).ToHashSet();
        foreach (var entry in state.SelectedEntries)
        {
            var mapping = state.AppliedByEntry.GetValueOrDefault(entry.Id);
            if (set.Status != OptionSetStatus.Active && mapping is null)
            {
                throw new ConflictException("An archived option set cannot add entries to an existing target");
            }

            if (mapping is null)
            {
                await OptionSetMaterializerEntryValidation.ValidateAsync(
                    context, set.Kind, entry, stagedProductIds, cancellationToken);
            }

            var row = await OptionSetMaterializerRows.FindAsync(
                context, target.Role, target, entry, mapping, cancellationToken);
            var isNewRow = row is null;
            row ??= await OptionSetMaterializerRows.CreateAsync(
                context, set.Kind, target, entry, GetOverride(target, entry.Id), audit, cancellationToken);

            var desired = OptionSetMaterializerRows.Desired(target.Role, entry, GetOverride(target, entry.Id));
            var current = OptionSetMaterializerRows.Current(row);
            var snapshot = mapping is null ? new Dictionary<string, System.Text.Json.JsonElement>()
                : OptionSetMaterializerRows.DeserializeSnapshot(mapping.LastAppliedValuesJson);
            var changed = ApplyResolvedValues(target, entry.Id, row, current, desired, snapshot, mapping is not null);
            var applied = mapping ?? new OptionSetAppliedRow
            {
                OptionSetAttachmentId = attachment.Id,
                OptionSetEntryId = entry.Id,
                RowType = row.RowType,
                MaterializedRowId = row.Id,
                OwnsMaterializedRow = row.OwnsRow,
                CreatedAt = now,
                CreatedBy = audit
            };
            applied.LastAppliedValuesJson = OptionSetMaterializerRows.SerializeSnapshot(desired);
            applied.UpdatedAt = mapping is null ? null : now;
            applied.UpdatedBy = mapping is null ? null : audit;
            if (mapping is null)
            {
                attachment.AppliedRows.Add(applied);
                await context.OptionSetAppliedRows.AddAsync(applied, cancellationToken);
            }

            if (changed)
            {
                Stamp(row.Entity, audit, now);
            }

            result.AppliedRows.Add(new OptionSetMaterializationAppliedRowDto
            {
                EntryId = entry.Id,
                RowType = row.RowType,
                RowId = row.Id,
                Action = isNewRow ? "add" : changed ? "update" : "preserve"
            });
        }

        await RemoveOmittedRowsAsync(context, attachment, state, desiredIds, audit, result, cancellationToken);
        ApplyAttachmentSettings(set.Kind, state, attachment, target, audit, now);
        await ValidateTargetRuntimeRulesAsync(context, set.Kind, state, cancellationToken);
        if (target.Role == OptionSetAttachmentRole.BundleChoice && state.MenuDefinition is not null)
        {
            state.MenuDefinition.VersionedSectionEditingStarted = true;
            state.MenuDefinition.AuthoringVersion++;
            state.MenuDefinition.UpdatedAt = now;
            state.MenuDefinition.UpdatedBy = audit;
            result.MenuAuthoringVersion = state.MenuDefinition.AuthoringVersion;
        }

        if (target.Role == OptionSetAttachmentRole.ProductChoice && state.CustomizationGroup is not null)
        {
            ApplyProductChoiceSettings(state.CustomizationGroup, state.Settings);
            state.CustomizationGroup.AuthoringVersion++;
            state.CustomizationGroup.UpdatedAt = now;
            state.CustomizationGroup.UpdatedBy = audit;
            result.CustomizationGroupVersion = state.CustomizationGroup.AuthoringVersion;
        }

        context.OptionSetAuthoringRevisions.Add(new OptionSetAuthoringRevision
        {
            OptionSetId = set.Id,
            TargetProductId = target.TargetProductId,
            TargetMenuSectionId = target.TargetMenuSectionId,
            TargetCustomizationGroupId = target.TargetCustomizationGroupId,
            SourceSetVersion = set.Version,
            AppliedSetVersion = set.Version,
            SummaryJson = System.Text.Json.JsonSerializer.Serialize(result.AppliedRows),
            CreatedAt = now,
            CreatedBy = audit
        });
        attachment.AppliedSetVersion = set.Version;
        attachment.LastIdempotencyKey = idempotencyKey;
        attachment.Version++;
        attachment.UpdatedAt = now;
        attachment.UpdatedBy = audit;
        result.Status = "applied";
        result.AttachmentId = attachment.Id;
        result.AttachmentVersion = attachment.Version;
        return result;
    }

    private static OptionSetAttachment NewAttachment(
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        string audit,
        DateTime now) => new()
        {
            OptionSetId = set.Id,
            Role = target.Role,
            TargetProductId = target.TargetProductId,
            TargetMenuSectionId = target.TargetMenuSectionId,
            TargetCustomizationGroupId = target.TargetCustomizationGroupId,
            AppliedSetVersion = set.Version,
            Version = 0,
            MinSelection = state.Settings.MinSelection,
            MaxSelection = state.Settings.MaxSelection,
            IncludedFree = state.Settings.IncludedFree,
            DisplayOrder = state.Settings.DisplayOrder ?? 0,
            IntentionalDifferenceReason = target.IntentionalDifferenceReason,
            CreatedAt = now,
            CreatedBy = audit
        };

    private static bool ApplyResolvedValues(
        OptionSetMaterializationTargetRequest target,
        Guid entryId,
        MaterializedOptionSetRow row,
        Dictionary<string, object?> current,
        IReadOnlyDictionary<string, object?> desired,
        Dictionary<string, System.Text.Json.JsonElement> baseline,
        bool hasMapping)
    {
        var fields = new Dictionary<string, object?>();
        var overrides = GetOverride(target, entryId);
        var overrideFields = OptionSetOverrideFields.From(overrides);
        foreach (var (field, next) in desired)
        {
            var hasBaseline = baseline.TryGetValue(field, out var previous);
            var isUnchanged = hasMapping && hasBaseline
                && OptionSetMaterializerRows.SnapshotEquals(previous, current[field]);
            var explicitOverride = target.ConflictPolicy == OptionSetConflictPolicy.UseOverrides
                && overrideFields.Contains(field);
            var shouldApply = target.ConflictPolicy == OptionSetConflictPolicy.UseSetValues
                || explicitOverride || isUnchanged;
            if (shouldApply && !Equals(current[field], next))
            {
                fields[field] = next;
            }
        }

        OptionSetMaterializerRows.ApplyValues(row, fields);
        return fields.Count > 0;
    }

    private static OptionSetEntryOverride? GetOverride(OptionSetMaterializationTargetRequest target, Guid entryId) =>
        target.ConflictPolicy == OptionSetConflictPolicy.UseOverrides
            && target.Overrides?.TryGetValue(entryId, out var value) == true ? value : null;

    private static void Stamp(object row, string audit, DateTime now)
    {
        switch (row)
        {
            case ProductIngredient ingredient:
                ingredient.UpdatedAt = now;
                ingredient.UpdatedBy = audit;
                break;
            case ProductSideItem side:
                side.UpdatedAt = now;
                side.UpdatedBy = audit;
                break;
            case MenuSectionItem item:
                item.UpdatedAt = now;
                item.UpdatedBy = audit;
                break;
            case ProductCustomizationProductOption option:
                option.UpdatedAt = now;
                option.UpdatedBy = audit;
                break;
        }
    }

    private static void ApplyAttachmentSettings(
        OptionSetKind kind,
        OptionSetTargetState state,
        OptionSetAttachment attachment,
        OptionSetMaterializationTargetRequest target,
        string audit,
        DateTime now)
    {
        attachment.MinSelection = state.Settings.MinSelection;
        attachment.MaxSelection = state.Settings.MaxSelection;
        attachment.IncludedFree = state.Settings.IncludedFree;
        attachment.DisplayOrder = state.Settings.DisplayOrder ?? 0;
        attachment.IntentionalDifferenceReason = string.IsNullOrWhiteSpace(target.IntentionalDifferenceReason)
            ? attachment.IntentionalDifferenceReason : target.IntentionalDifferenceReason.Trim();

        if (kind == OptionSetKind.Sauce)
        {
            state.Product.SauceMin = state.Settings.MinSelection ?? 0;
            state.Product.SauceMax = state.Settings.MaxSelection;
            state.Product.SauceIncludedFree = state.Settings.IncludedFree ?? 0;
            state.Product.UpdatedAt = now;
            state.Product.UpdatedBy = audit;
        }

        if (kind == OptionSetKind.BundleChoice && state.Section is not null)
        {
            state.Section.MinSelection = state.Settings.MinSelection ?? 0;
            state.Section.MaxSelection = state.Settings.MaxSelection ?? 1;
            state.Section.IsRequired = state.Section.MinSelection > 0;
            state.Section.DisplayOrder = state.Settings.DisplayOrder ?? state.Section.DisplayOrder;
        }

    }

    private static void ApplyProductChoiceSettings(
        ProductCustomizationGroup group,
        OptionSetAttachmentSettings settings)
    {
        group.MinSelection = settings.MinSelection ?? 0;
        group.MaxSelection = settings.MaxSelection ?? 1;
        group.IsRequired = group.MinSelection > 0;
        group.IncludedFreeUnits = settings.IncludedFree ?? 0;
        group.DisplayOrder = settings.DisplayOrder ?? group.DisplayOrder;
    }

    private static async Task RemoveOmittedRowsAsync(
        ApplicationDbContext context,
        OptionSetAttachment attachment,
        OptionSetTargetState state,
        HashSet<Guid> selectedIds,
        string audit,
        OptionSetMaterializationTargetResultDto result,
        CancellationToken cancellationToken)
    {
        foreach (var mapping in attachment.AppliedRows.Where(row => !selectedIds.Contains(row.OptionSetEntryId)).ToList())
        {
            var row = await OptionSetMaterializerRows.FindAsync(
                context, attachment.Role, new OptionSetMaterializationTargetRequest
                {
                    TargetProductId = attachment.TargetProductId,
                    TargetMenuSectionId = attachment.TargetMenuSectionId,
                    TargetCustomizationGroupId = attachment.TargetCustomizationGroupId,
                    Role = attachment.Role
                },
                state.SelectedEntries.FirstOrDefault() ?? new OptionSetEntry { CreatedBy = audit }, mapping, cancellationToken);
            if (row is not null && mapping.OwnsMaterializedRow
                && !await context.OptionSetAppliedRows.AnyAsync(other => other.Id != mapping.Id
                    && other.RowType == mapping.RowType && other.MaterializedRowId == mapping.MaterializedRowId, cancellationToken))
            {
                OptionSetMaterializerRows.Delete(context, row);
            }

            context.OptionSetAppliedRows.Remove(mapping);
            attachment.AppliedRows.Remove(mapping);
            result.AppliedRows.Add(new OptionSetMaterializationAppliedRowDto
            {
                EntryId = mapping.OptionSetEntryId,
                RowType = mapping.RowType,
                RowId = mapping.MaterializedRowId,
                Action = "remove"
            });
        }
    }

    private static async Task ValidateTargetRuntimeRulesAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetTargetState state,
        CancellationToken cancellationToken)
    {
        if (kind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            await ValidateIncludedDeductionAsync(context, state.Product, cancellationToken);
        }

        if (kind == OptionSetKind.BundleChoice && state.Section is not null)
        {
            await MenuSectionVariationValidator.ValidateEntitiesAsync(context, [state.Section], cancellationToken);
        }
    }

    private static async Task ValidateIncludedDeductionAsync(
        ApplicationDbContext context,
        Product product,
        CancellationToken cancellationToken)
    {
        await context.ProductIngredients.Where(ingredient => ingredient.ProductId == product.Id).LoadAsync(cancellationToken);
        var activeVariations = await context.ProductVariations.Where(variation => variation.ProductId == product.Id && variation.IsActive)
            .Select(variation => variation.PriceModifier).ToListAsync(cancellationToken);
        var dto = context.ProductIngredients.Local.Where(ingredient => ingredient.ProductId == product.Id)
            .Select(ingredient => new ProductIngredientDto
            {
                IsOptional = ingredient.IsOptional,
                IsIncludedInBasePrice = ingredient.IsIncludedInBasePrice,
                IsActive = ingredient.IsActive,
                Price = ingredient.Price
            }).ToList();
        var deduction = IncludedInBaseDeductionRule.MaxDeduction(dto);
        var minPrice = IncludedInBaseDeductionRule.MinEffectiveUnitPrice(product.BasePrice, product.HideBaseProduct, activeVariations);
        if (!IncludedInBaseDeductionRule.Fits(deduction, minPrice))
        {
            throw new BadRequestException(IncludedInBaseDeductionRule.BuildMessage(deduction, minPrice));
        }
    }
}
