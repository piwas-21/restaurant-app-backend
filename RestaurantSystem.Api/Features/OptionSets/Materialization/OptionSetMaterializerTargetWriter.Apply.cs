using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetMaterializerTargetWriter
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
        var result = CreateTargetResult(target, state);
        if (IsIdempotentReplay(state, set, idempotencyKey))
        {
            PopulateUnchangedResult(result, state);
            return result;
        }

        var now = DateTime.UtcNow;
        var attachment = await EnsureAttachmentAsync(context, set, target, state, audit, now, cancellationToken);
        await ValidateEntriesToAddAsync(context, set, state, stagedProductIds, cancellationToken);
        var rowLookups = await OptionSetMaterializerBatchRows.FindManyAsync(
            context, target.Role, target, state.SelectedEntries, state.AppliedByEntry, cancellationToken);
        var selectedIds = state.SelectedEntries.Select(entry => entry.Id).ToHashSet();
        await ApplySelectedRowsAsync(new TargetRowApplyContext
        {
            DbContext = context,
            Set = set,
            Target = target,
            State = state,
            Attachment = attachment,
            RowLookups = rowLookups,
            Audit = audit,
            Now = now,
            Result = result,
            CancellationToken = cancellationToken
        });
        await RemoveOmittedRowsAsync(context, attachment, selectedIds, rowLookups, result, cancellationToken);
        OptionSetMaterializerTargetSettings.ApplyAttachmentSettings(set.Kind, state, attachment, target, audit, now);
        await OptionSetMaterializerRuntimeRules.ValidateAsync(context, set.Kind, state, cancellationToken);
        AdvanceTargetVersion(target, state, result, audit, now);
        AddRevision(context, set, target, result, audit, now);
        UpdateAttachment(attachment, set, idempotencyKey, audit, now);
        result.Status = "applied";
        result.AttachmentId = attachment.Id;
        result.AttachmentVersion = attachment.Version;
        return result;
    }

    private static OptionSetMaterializationTargetResultDto CreateTargetResult(
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state) => new()
        {
            TargetKey = target.TargetKey,
            AttachmentId = state.Attachment?.Id,
            AttachmentVersion = state.Attachment?.Version
        };

    private static bool IsIdempotentReplay(OptionSetTargetState state, OptionSet set, string idempotencyKey) =>
        state.Attachment?.LastIdempotencyKey == idempotencyKey
        && state.Attachment.AppliedSetVersion == set.Version;

    private static void PopulateUnchangedResult(
        OptionSetMaterializationTargetResultDto result,
        OptionSetTargetState state)
    {
        result.Status = "unchanged";
        result.AppliedRows = state.Attachment!.AppliedRows.Select(row => new OptionSetMaterializationAppliedRowDto
        {
            EntryId = row.OptionSetEntryId,
            RowType = row.RowType,
            RowId = row.MaterializedRowId,
            Action = "unchanged"
        }).ToList();
        result.MenuAuthoringVersion = state.MenuDefinition?.AuthoringVersion;
        result.CustomizationGroupVersion = state.CustomizationGroup?.AuthoringVersion;
    }

    private static async Task<OptionSetAttachment> EnsureAttachmentAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        string audit,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (state.Attachment is not null)
        {
            return state.Attachment;
        }

        var attachment = NewAttachment(set, target, state, audit, now);
        await context.OptionSetAttachments.AddAsync(attachment, cancellationToken);
        return attachment;
    }

    private static async Task ValidateEntriesToAddAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetTargetState state,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var entries = state.SelectedEntries.Where(entry => !state.AppliedByEntry.ContainsKey(entry.Id)).ToList();
        if (set.Status != OptionSetStatus.Active && entries.Count > 0)
        {
            throw new ConflictException("An archived option set cannot add entries to an existing target");
        }

        var errors = await OptionSetMaterializerEntryValidation.ValidateManyAsync(
            context, set.Kind, entries, stagedProductIds, cancellationToken);
        if (errors.FirstOrDefault(error => error is not null) is string error)
        {
            throw new BadRequestException(error);
        }
    }

    private static void AdvanceTargetVersion(
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state,
        OptionSetMaterializationTargetResultDto result,
        string audit,
        DateTime now)
    {
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
    }

    private static void AddRevision(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        OptionSetMaterializationTargetResultDto result,
        string audit,
        DateTime now)
    {
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
    }

    private static void UpdateAttachment(
        OptionSetAttachment attachment,
        OptionSet set,
        string idempotencyKey,
        string audit,
        DateTime now)
    {
        attachment.AppliedSetVersion = set.Version;
        attachment.LastIdempotencyKey = idempotencyKey;
        attachment.Version++;
        attachment.UpdatedAt = now;
        attachment.UpdatedBy = audit;
    }
}
