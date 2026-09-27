using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Menus;
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

        var entriesToValidate = state.SelectedEntries
            .Where(entry => !state.AppliedByEntry.ContainsKey(entry.Id)).ToList();
        if (set.Status != OptionSetStatus.Active && entriesToValidate.Count > 0)
        {
            throw new ConflictException("An archived option set cannot add entries to an existing target");
        }

        var validationErrors = await OptionSetMaterializerEntryValidation.ValidateManyAsync(
            context, set.Kind, entriesToValidate, stagedProductIds, cancellationToken);
        if (validationErrors.FirstOrDefault(error => error is not null) is string validationError)
        {
            throw new BadRequestException(validationError);
        }

        var rowLookups = await OptionSetMaterializerBatchRows.FindManyAsync(
            context, target.Role, target, state.SelectedEntries, state.AppliedByEntry, cancellationToken);
        var desiredIds = state.SelectedEntries.Select(entry => entry.Id).ToHashSet();
        foreach (var entry in state.SelectedEntries)
        {
            var mapping = state.AppliedByEntry.GetValueOrDefault(entry.Id);
            if (set.Status != OptionSetStatus.Active && mapping is null)
            {
                throw new ConflictException("An archived option set cannot add entries to an existing target");
            }

            var resolution = rowLookups[entry.Id];
            if (resolution.ConflictMessage is string conflict)
            {
                throw new ConflictException(conflict);
            }

            var row = resolution.Row;
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
                OptionSetMaterializerTargetAudit.Stamp(row.Entity, audit, now);
            }

            result.AppliedRows.Add(new OptionSetMaterializationAppliedRowDto
            {
                EntryId = entry.Id,
                RowType = row.RowType,
                RowId = row.Id,
                Action = isNewRow ? "add" : changed ? "update" : "preserve"
            });
        }

        await RemoveOmittedRowsAsync(
            context, attachment, desiredIds, rowLookups, result, cancellationToken);
        OptionSetMaterializerTargetSettings.ApplyAttachmentSettings(set.Kind, state, attachment, target, audit, now);
        await OptionSetMaterializerRuntimeRules.ValidateAsync(context, set.Kind, state, cancellationToken);
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
            OptionSetMaterializerTargetSettings.ApplyProductChoiceSettings(state.CustomizationGroup, state.Settings);
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
