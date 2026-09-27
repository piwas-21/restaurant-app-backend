using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetMaterializerTargetWriter
{
    private static async Task ApplySelectedRowsAsync(TargetRowApplyContext context)
    {
        foreach (var entry in context.State.SelectedEntries)
        {
            await ApplySelectedRowAsync(context, entry, context.RowLookups[entry.Id]);
        }
    }

    private static async Task ApplySelectedRowAsync(
        TargetRowApplyContext context,
        OptionSetEntry entry,
        MaterializedOptionSetRowLookup resolution)
    {
        var mapping = context.State.AppliedByEntry.GetValueOrDefault(entry.Id);
        if (context.Set.Status != OptionSetStatus.Active && mapping is null)
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
            context.DbContext,
            context.Set.Kind,
            context.Target,
            entry,
            GetOverride(context.Target, entry.Id),
            context.Audit,
            context.CancellationToken);
        var desired = OptionSetMaterializerRows.Desired(
            context.Target.Role, entry, GetOverride(context.Target, entry.Id));
        var current = OptionSetMaterializerRows.Current(row);
        var snapshot = mapping is null
            ? new Dictionary<string, System.Text.Json.JsonElement>()
            : OptionSetMaterializerRows.DeserializeSnapshot(mapping.LastAppliedValuesJson);
        var changed = ApplyResolvedValues(
            context.Target, entry.Id, row, current, desired, snapshot, mapping is not null);
        var applied = mapping ?? NewAppliedRow(context.Attachment, entry, row, context.Audit, context.Now);
        applied.LastAppliedValuesJson = OptionSetMaterializerRows.SerializeSnapshot(desired);
        applied.UpdatedAt = mapping is null ? null : context.Now;
        applied.UpdatedBy = mapping is null ? null : context.Audit;
        if (mapping is null)
        {
            context.Attachment.AppliedRows.Add(applied);
            await context.DbContext.OptionSetAppliedRows.AddAsync(applied, context.CancellationToken);
        }

        if (changed)
        {
            OptionSetMaterializerTargetAudit.Stamp(row.Entity, context.Audit, context.Now);
        }

        context.Result.AppliedRows.Add(new OptionSetMaterializationAppliedRowDto
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

    private static string ResolveAction(bool isNewRow, bool changed)
    {
        if (isNewRow)
        {
            return "add";
        }

        return changed ? "update" : "preserve";
    }

    private sealed class TargetRowApplyContext
    {
        public required ApplicationDbContext DbContext { get; init; }
        public required OptionSet Set { get; init; }
        public required OptionSetMaterializationTargetRequest Target { get; init; }
        public required OptionSetTargetState State { get; init; }
        public required OptionSetAttachment Attachment { get; init; }
        public required IReadOnlyDictionary<Guid, MaterializedOptionSetRowLookup> RowLookups { get; init; }
        public required string Audit { get; init; }
        public required DateTime Now { get; init; }
        public required OptionSetMaterializationTargetResultDto Result { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
