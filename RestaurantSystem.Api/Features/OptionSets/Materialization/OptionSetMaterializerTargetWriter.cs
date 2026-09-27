using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetMaterializerTargetWriter
{
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

    private static async Task RemoveOmittedRowsAsync(
        ApplicationDbContext context,
        OptionSetAttachment attachment,
        HashSet<Guid> selectedIds,
        IReadOnlyDictionary<Guid, MaterializedOptionSetRowLookup> rowLookups,
        OptionSetMaterializationTargetResultDto result,
        CancellationToken cancellationToken)
    {
        var omittedMappings = attachment.AppliedRows.Where(row => !selectedIds.Contains(row.OptionSetEntryId)).ToList();
        var sharedOwnedRows = await OptionSetMaterializerBatchRows.LoadSharedOwnedRowIdsAsync(
            context, omittedMappings, cancellationToken);
        foreach (var mapping in omittedMappings)
        {
            var resolution = rowLookups[mapping.OptionSetEntryId];
            if (resolution.ConflictMessage is string conflict)
            {
                throw new ConflictException(conflict);
            }

            if (resolution.Row is not null && mapping.OwnsMaterializedRow
                && !sharedOwnedRows.Contains(mapping.MaterializedRowId))
            {
                OptionSetMaterializerRows.Delete(context, resolution.Row);
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

}
