using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetMaterializerTargetWriter
{
    private static async Task ApplySelectedRowsAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        OptionSetAttachment attachment,
        IReadOnlyDictionary<Guid, MaterializedOptionSetRowLookup> rowLookups,
        string audit,
        DateTime now,
        OptionSetMaterializationTargetResultDto result,
        CancellationToken cancellationToken)
    {
        foreach (var entry in state.SelectedEntries)
        {
            await ApplySelectedRowAsync(
                context, set, target, state, attachment, rowLookups[entry.Id], entry, audit, now, result, cancellationToken);
        }
    }

    private static async Task ApplySelectedRowAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        OptionSetAttachment attachment,
        MaterializedOptionSetRowLookup resolution,
        OptionSetEntry entry,
        string audit,
        DateTime now,
        OptionSetMaterializationTargetResultDto result,
        CancellationToken cancellationToken)
    {
        var mapping = state.AppliedByEntry.GetValueOrDefault(entry.Id);
        if (set.Status != OptionSetStatus.Active && mapping is null)
        {
            throw new ConflictException("An archived option set cannot add entries to an existing target");
        }

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
        var applied = mapping ?? NewAppliedRow(attachment, entry, row, audit, now);
        await PersistAppliedRowAsync(context, attachment, applied, mapping, desired, audit, now, cancellationToken);
        if (changed)
        {
            OptionSetMaterializerTargetAudit.Stamp(row.Entity, audit, now);
        }

        result.AppliedRows.Add(new OptionSetMaterializationAppliedRowDto
        {
            EntryId = entry.Id,
            RowType = row.RowType,
            RowId = row.Id,
            Action = ResolveAction(isNewRow, changed)
        });
    }

    private static OptionSetAppliedRow NewAppliedRow(
        OptionSetAttachment attachment,
        OptionSetEntry entry,
        MaterializedOptionSetRow row,
        string audit,
        DateTime now) => new()
        {
            OptionSetAttachmentId = attachment.Id,
            OptionSetEntryId = entry.Id,
            RowType = row.RowType,
            MaterializedRowId = row.Id,
            OwnsMaterializedRow = row.OwnsRow,
            CreatedAt = now,
            CreatedBy = audit
        };

    private static async Task PersistAppliedRowAsync(
        ApplicationDbContext context,
        OptionSetAttachment attachment,
        OptionSetAppliedRow applied,
        OptionSetAppliedRow? existingMapping,
        IReadOnlyDictionary<string, object?> desired,
        string audit,
        DateTime now,
        CancellationToken cancellationToken)
    {
        applied.LastAppliedValuesJson = OptionSetMaterializerRows.SerializeSnapshot(desired);
        applied.UpdatedAt = existingMapping is null ? null : now;
        applied.UpdatedBy = existingMapping is null ? null : audit;
        if (existingMapping is not null)
        {
            return;
        }

        attachment.AppliedRows.Add(applied);
        await context.OptionSetAppliedRows.AddAsync(applied, cancellationToken);
    }

    private static string ResolveAction(bool isNewRow, bool changed)
    {
        if (isNewRow)
        {
            return "add";
        }

        return changed ? "update" : "preserve";
    }

}
