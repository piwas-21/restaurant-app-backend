using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerPlanBuilder
{
    public static async Task<OptionSetMaterializationTargetPreviewDto> BuildAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var preview = new OptionSetMaterializationTargetPreviewDto
        {
            TargetKey = target.TargetKey,
            TargetProductId = target.TargetProductId,
            TargetMenuSectionId = target.TargetMenuSectionId,
            TargetCustomizationGroupId = target.TargetCustomizationGroupId
        };
        try
        {
            var state = await OptionSetMaterializerTargetLoader.LoadAsync(
                context, set, target, null, stagedProductIds, cancellationToken);
            preview.AttachmentId = state.Attachment?.Id;
            preview.CurrentAttachmentVersion = state.Attachment?.Version;
            preview.CurrentMenuAuthoringVersion = state.MenuDefinition?.AuthoringVersion;
            preview.CurrentCustomizationGroupVersion = state.CustomizationGroup?.AuthoringVersion;
            preview.CurrentSettings = CopySettings(state.CurrentSettings);
            preview.ProposedSettings = CopySettings(state.Settings);
            AddSettingsDiff(state.CurrentSettings, state.Settings, preview.ChangedSettings);
            await PlanActiveEntries(context, set, target, state, preview, stagedProductIds, cancellationToken);
            PlanRemovedEntries(state, preview);
            var hasRowChanges = preview.Changes.Any(change =>
                change.Action is "add" or "update" or "remove" || change.ChangedFields.Count > 0);
            SetPreviewStatus(preview, hasRowChanges);
        }
        catch (ConflictException exception)
        {
            preview.Status = "conflict";
            preview.Conflicts.Add(new OptionSetMaterializationConflictDto
            {
                Code = "stale-or-invalid-target",
                Message = exception.Message
            });
        }

        return preview;
    }

    private static async Task PlanActiveEntries(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        OptionSetMaterializationTargetPreviewDto preview,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var entriesToValidate = state.SelectedEntries
            .Where(entry => !state.AppliedByEntry.ContainsKey(entry.Id)).ToList();
        if (set.Status == RestaurantSystem.Domain.Common.Enums.OptionSetStatus.Active)
        {
            var validationErrors = await OptionSetMaterializerEntryValidation.ValidateManyAsync(
                context, set.Kind, entriesToValidate, stagedProductIds, cancellationToken);
            if (validationErrors.FirstOrDefault(error => error is not null) is string error)
            {
                throw new BadRequestException(error);
            }
        }

        var rows = await OptionSetMaterializerBatchRows.FindManyAsync(
            context, target.Role, target, state.SelectedEntries, state.AppliedByEntry, cancellationToken);
        foreach (var entry in state.SelectedEntries)
        {
            state.AppliedByEntry.TryGetValue(entry.Id, out var mapping);
            if (set.Status != RestaurantSystem.Domain.Common.Enums.OptionSetStatus.Active && mapping is null)
            {
                preview.Conflicts.Add(new OptionSetMaterializationConflictDto
                {
                    Code = "row-conflict",
                    Message = "An archived option set cannot add entries to an existing target",
                    EntryId = entry.Id
                });
                continue;
            }

            var resolution = rows[entry.Id];
            if (resolution.ConflictMessage is string conflict)
            {
                preview.Conflicts.Add(new OptionSetMaterializationConflictDto
                {
                    Code = "row-conflict",
                    Message = conflict,
                    EntryId = entry.Id
                });
                continue;
            }

            preview.Changes.Add(PlanEntry(target, entry, mapping, resolution.Row));
        }
    }

    private static OptionSetAttachmentSettings CopySettings(OptionSetAttachmentSettings settings) => new()
    {
        MinSelection = settings.MinSelection,
        MaxSelection = settings.MaxSelection,
        IncludedFree = settings.IncludedFree,
        DisplayOrder = settings.DisplayOrder
    };

    private static void AddSettingsDiff(
        OptionSetAttachmentSettings current,
        OptionSetAttachmentSettings proposed,
        ICollection<string> changes)
    {
        AddIfChanged(nameof(current.MinSelection), current.MinSelection, proposed.MinSelection, changes);
        AddIfChanged(nameof(current.MaxSelection), current.MaxSelection, proposed.MaxSelection, changes);
        AddIfChanged(nameof(current.IncludedFree), current.IncludedFree, proposed.IncludedFree, changes);
        AddIfChanged(nameof(current.DisplayOrder), current.DisplayOrder, proposed.DisplayOrder, changes);
    }

    private static void AddIfChanged<T>(string field, T current, T proposed, ICollection<string> changes)
    {
        if (!EqualityComparer<T>.Default.Equals(current, proposed))
        {
            changes.Add(char.ToLowerInvariant(field[0]) + field[1..]);
        }
    }

    private static OptionSetMaterializationChangeDto PlanEntry(
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        OptionSetAppliedRow? mapping,
        MaterializedOptionSetRow? row)
    {
        var entryOverride = ResolveOverride(target, entry.Id);
        var desired = OptionSetMaterializerRows.Desired(target.Role, entry, entryOverride);
        var change = new OptionSetMaterializationChangeDto
        {
            EntryId = entry.Id,
            RowType = RowTypeFor(target.Role),
            RowId = row?.Id
        };
        if (row is null)
        {
            change.Action = "add";
            change.ChangedFields.AddRange(desired.Keys);
            return change;
        }

        var current = OptionSetMaterializerRows.Current(row);
        var baseline = mapping is null
            ? desired.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
            : OptionSetMaterializerRows.DeserializeSnapshot(mapping.LastAppliedValuesJson);
        foreach (var (field, value) in desired)
        {
            PlanFieldChange(target.ConflictPolicy, mapping is not null, entryOverride,
                field, value, current[field], baseline, change);
        }

        change.Action = change.ChangedFields.Count > 0 ? "update" : "preserve";
        return change;
    }

    private static OptionSetEntryOverride? ResolveOverride(
        OptionSetMaterializationTargetRequest target,
        Guid entryId)
    {
        if (target.ConflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseOverrides
            && target.Overrides?.TryGetValue(entryId, out var supplied) == true)
        {
            return supplied;
        }

        return null;
    }

    private static void PlanFieldChange(
        RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy conflictPolicy,
        bool hasMapping,
        OptionSetEntryOverride? entryOverride,
        string field,
        object? desired,
        object? current,
        Dictionary<string, JsonElement> baseline,
        OptionSetMaterializationChangeDto change)
    {
        if (Equals(desired, current))
        {
            return;
        }

        var explicitOverrides = OptionSetOverrideFields.From(entryOverride);
        var equalsBaseline = baseline.TryGetValue(field, out var previous)
            && OptionSetMaterializerRows.SnapshotEquals(previous, current);
        var shouldApply = conflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseSetValues
            || (conflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseOverrides
                && explicitOverrides.Contains(field))
            || (hasMapping && equalsBaseline);
        (shouldApply ? change.ChangedFields : change.PreservedFields).Add(field);
    }

    private static void SetPreviewStatus(OptionSetMaterializationTargetPreviewDto preview, bool hasRowChanges)
    {
        if (preview.Conflicts.Count > 0)
        {
            preview.Status = "conflict";
        }
        else if (!hasRowChanges && preview.ChangedSettings.Count == 0)
        {
            preview.Status = "unchanged";
        }
        else
        {
            preview.Status = "ready";
        }
    }

    private static void PlanRemovedEntries(
        OptionSetTargetState state,
        OptionSetMaterializationTargetPreviewDto preview)
    {
        var selectedIds = state.SelectedEntries.Select(entry => entry.Id).ToHashSet();
        foreach (var mapping in state.AppliedByEntry.Values.Where(row => !selectedIds.Contains(row.OptionSetEntryId)))
        {
            preview.Changes.Add(new OptionSetMaterializationChangeDto
            {
                EntryId = mapping.OptionSetEntryId,
                RowType = mapping.RowType,
                RowId = mapping.MaterializedRowId,
                Action = "remove"
            });
        }
    }

    private static string RowTypeFor(RestaurantSystem.Domain.Common.Enums.OptionSetAttachmentRole role) => role switch
    {
        RestaurantSystem.Domain.Common.Enums.OptionSetAttachmentRole.Ingredient or RestaurantSystem.Domain.Common.Enums.OptionSetAttachmentRole.Sauce => OptionSetMaterializerRows.IngredientRow,
        RestaurantSystem.Domain.Common.Enums.OptionSetAttachmentRole.SuggestedSide => OptionSetMaterializerRows.SideRow,
        RestaurantSystem.Domain.Common.Enums.OptionSetAttachmentRole.ProductChoice => OptionSetMaterializerRows.ProductChoiceRow,
        _ => OptionSetMaterializerRows.BundleRow
    };

}
