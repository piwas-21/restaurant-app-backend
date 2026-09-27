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
            var state = await OptionSetMaterializerTargetLoader.LoadAsync(context, set, target, null, cancellationToken);
            preview.AttachmentId = state.Attachment?.Id;
            preview.CurrentAttachmentVersion = state.Attachment?.Version;
            preview.CurrentMenuAuthoringVersion = state.MenuDefinition?.AuthoringVersion;
            preview.CurrentCustomizationGroupVersion = state.CustomizationGroup?.AuthoringVersion;
            await PlanActiveEntries(context, set, target, state, preview, cancellationToken);
            PlanRemovedEntries(state, preview);
            preview.Status = preview.Conflicts.Count > 0 ? "conflict"
                : preview.Changes.Count == 0 ? "unchanged" : "ready";
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
        CancellationToken cancellationToken)
    {
        foreach (var entry in state.SelectedEntries)
        {
            try
            {
                state.AppliedByEntry.TryGetValue(entry.Id, out var mapping);
                if (set.Status != RestaurantSystem.Domain.Common.Enums.OptionSetStatus.Active && mapping is null)
                {
                    throw new ConflictException("An archived option set cannot add entries to an existing target");
                }

                if (mapping is null)
                {
                    await OptionSetMaterializerEntryValidation.ValidateAsync(context, set.Kind, entry, cancellationToken);
                }

                var row = await OptionSetMaterializerRows.FindAsync(
                    context, target.Role, target, entry, mapping, cancellationToken);
                preview.Changes.Add(PlanEntry(target, entry, mapping, row));
            }
            catch (ConflictException exception)
            {
                preview.Conflicts.Add(new OptionSetMaterializationConflictDto
                {
                    Code = "row-conflict",
                    Message = exception.Message,
                    EntryId = entry.Id
                });
            }
        }
    }

    private static OptionSetMaterializationChangeDto PlanEntry(
        OptionSetMaterializationTargetRequest target,
        OptionSetEntry entry,
        OptionSetAppliedRow? mapping,
        MaterializedOptionSetRow? row)
    {
        var entryOverride = target.ConflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseOverrides
            && target.Overrides?.TryGetValue(entry.Id, out var supplied) == true ? supplied : null;
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
        var explicitOverrides = OptionSetOverrideFields.From(entryOverride);
        foreach (var (field, value) in desired)
        {
            var equalsBaseline = baseline.TryGetValue(field, out var previous)
                && OptionSetMaterializerRows.SnapshotEquals(previous, current[field]);
            var overwrite = target.ConflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseSetValues
                || (target.ConflictPolicy == RestaurantSystem.Domain.Common.Enums.OptionSetConflictPolicy.UseOverrides
                    && explicitOverrides.Contains(field));
            if (overwrite || (mapping is not null && equalsBaseline))
            {
                if (!Equals(value, current[field]))
                {
                    change.ChangedFields.Add(field);
                }
            }
            else if (!Equals(value, current[field]))
            {
                change.PreservedFields.Add(field);
            }
        }

        change.Action = change.ChangedFields.Count > 0 ? "update"
            : change.PreservedFields.Count > 0 ? "preserve" : "preserve";
        return change;
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
