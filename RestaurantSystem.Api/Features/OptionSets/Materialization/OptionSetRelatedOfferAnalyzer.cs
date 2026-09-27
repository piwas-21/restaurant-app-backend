using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetRelatedOfferAnalyzer
{
    public static async Task<IReadOnlyList<OptionSetRelatedOfferWarningDto>> AnalyzeAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        if (set.Kind != OptionSetKind.BundleChoice)
        {
            return [];
        }

        var choiceTargets = request.Targets.Where(IsChoiceTarget).ToList();
        var sourceTargets = await LoadSourceTargetsAsync(
            context, set, choiceTargets, stagedProductIds, cancellationToken);
        var requiredRequestTargets = BuildRequiredRequestTargets(choiceTargets, sourceTargets);
        var warnings = new List<OptionSetRelatedOfferWarningDto>();
        foreach (var target in choiceTargets)
        {
            if (!TryGetRequiredSource(target, sourceTargets, out var source))
            {
                continue;
            }

            var targetWarnings = await AnalyzeRequiredTargetAsync(
                context, set, target, source, requiredRequestTargets, cancellationToken);
            warnings.AddRange(targetWarnings);
        }

        return warnings
            .DistinctBy(warning => (warning.TargetKey, warning.RelatedProductId, warning.RelatedTargetId))
            .ToList();
    }

    private static async Task<Dictionary<string, SourceTarget?>> LoadSourceTargetsAsync(
        ApplicationDbContext context,
        OptionSet set,
        IReadOnlyCollection<OptionSetMaterializationTargetRequest> choiceTargets,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var sourceTargets = new Dictionary<string, SourceTarget?>(StringComparer.Ordinal);
        foreach (var target in choiceTargets)
        {
            var source = await LoadSourceAsync(context, set, target, stagedProductIds, cancellationToken);
            sourceTargets[target.TargetKey] = source;
        }

        return sourceTargets;
    }

    private static Dictionary<(Guid ProductId, OptionSetAttachmentRole Role), HashSet<Guid?>>
        BuildRequiredRequestTargets(
            IReadOnlyCollection<OptionSetMaterializationTargetRequest> choiceTargets,
            IReadOnlyDictionary<string, SourceTarget?> sourceTargets)
    {
        var requiredRequestTargets = new Dictionary<(Guid ProductId, OptionSetAttachmentRole Role), HashSet<Guid?>>();
        foreach (var target in choiceTargets)
        {
            if (!TryGetRequiredSource(target, sourceTargets, out var source))
            {
                continue;
            }

            var targetId = GetTargetId(target);
            var key = (target.TargetProductId, target.Role);
            if (!requiredRequestTargets.TryGetValue(key, out var targetIds))
            {
                targetIds = [];
                requiredRequestTargets.Add(key, targetIds);
            }

            targetIds.Add(targetId);
        }

        return requiredRequestTargets;
    }

    private static bool TryGetRequiredSource(
        OptionSetMaterializationTargetRequest target,
        IReadOnlyDictionary<string, SourceTarget?> sourceTargets,
        [NotNullWhen(true)] out SourceTarget? source)
    {
        if (sourceTargets.TryGetValue(target.TargetKey, out var found)
            && found is not null && found.State.Settings.MinSelection > 0)
        {
            source = found;
            return true;
        }

        source = null;
        return false;
    }

    private static async Task<List<OptionSetRelatedOfferWarningDto>> AnalyzeRequiredTargetAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        SourceTarget source,
        IReadOnlyDictionary<(Guid ProductId, OptionSetAttachmentRole Role), HashSet<Guid?>> requiredRequestTargets,
        CancellationToken cancellationToken)
    {
        var offerRootId = ResolveOfferRootId(target, source.State);
        if (offerRootId is null)
        {
            return [];
        }

        var candidates = await LoadRelatedTargetsAsync(
            context,
            offerRootId.Value,
            source.State.MenuDefinition?.ParentOfferVariationId,
            source.State.SelectedEntries,
            cancellationToken);
        var attachments = await LoadRequiredAttachmentsAsync(context, set.Id, offerRootId.Value, source, candidates, cancellationToken);
        return BuildWarnings(target, source, candidates, attachments, requiredRequestTargets);
    }

    private static Guid? ResolveOfferRootId(
        OptionSetMaterializationTargetRequest target,
        OptionSetTargetState state) => target.Role == OptionSetAttachmentRole.ProductChoice
        ? state.Product.Id
        : state.MenuDefinition?.ParentOfferProductId;

    private static Guid? GetTargetId(OptionSetMaterializationTargetRequest target) =>
        target.Role == OptionSetAttachmentRole.ProductChoice
            ? target.TargetCustomizationGroupId
            : target.TargetMenuSectionId;

    private static async Task<List<ExistingAttachment>> LoadRequiredAttachmentsAsync(
        ApplicationDbContext context,
        Guid optionSetId,
        Guid offerRootId,
        SourceTarget source,
        IReadOnlyCollection<RelatedTarget> candidates,
        CancellationToken cancellationToken)
    {
        var candidateProductIds = candidates.Select(candidate => candidate.ProductId).ToArray();
        return await context.OptionSetAttachments.AsNoTracking()
            .Where(item => item.OptionSetId == optionSetId && item.MinSelection > 0
                && (item.TargetProductId == offerRootId
                    || item.TargetProductId == source.State.Product.Id
                    || candidateProductIds.Contains(item.TargetProductId)))
            .Select(item => new ExistingAttachment(
                item.TargetProductId, item.TargetMenuSectionId, item.TargetCustomizationGroupId, item.Role))
            .ToListAsync(cancellationToken);
    }

    private static List<OptionSetRelatedOfferWarningDto> BuildWarnings(
        OptionSetMaterializationTargetRequest target,
        SourceTarget source,
        IReadOnlyCollection<RelatedTarget> candidates,
        IReadOnlyCollection<ExistingAttachment> attachments,
        IReadOnlyDictionary<(Guid ProductId, OptionSetAttachmentRole Role), HashSet<Guid?>> requiredRequestTargets)
    {
        var warnings = new List<OptionSetRelatedOfferWarningDto>();
        foreach (var candidate in candidates)
        {
            if (HasRequiredAttachment(candidate, attachments)
                || IsRequiredRequestTarget(candidate, requiredRequestTargets))
            {
                continue;
            }

            warnings.Add(CreateWarning(target, source, candidate));
        }

        return warnings;
    }

    private static OptionSetRelatedOfferWarningDto CreateWarning(
        OptionSetMaterializationTargetRequest target,
        SourceTarget source,
        RelatedTarget candidate) => new()
        {
            TargetKey = target.TargetKey,
            RelatedProductId = candidate.ProductId,
            RelatedProductName = candidate.ProductName,
            RelatedOfferType = candidate.Role == OptionSetAttachmentRole.ProductChoice ? "standalone" : "menu",
            RelatedVariationId = candidate.ParentVariationId,
            RelatedTargetRole = candidate.Role == OptionSetAttachmentRole.ProductChoice ? "productChoice" : "bundleChoice",
            RelatedTargetId = candidate.TargetId,
            RelatedTargetName = candidate.TargetName,
            ReasonRequired = string.IsNullOrWhiteSpace(target.IntentionalDifferenceReason)
            && string.IsNullOrWhiteSpace(source.State.Attachment?.IntentionalDifferenceReason)
        };

    private static bool IsChoiceTarget(OptionSetMaterializationTargetRequest target) =>
        target.Role is OptionSetAttachmentRole.ProductChoice or OptionSetAttachmentRole.BundleChoice;

    private static async Task<SourceTarget?> LoadSourceAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await OptionSetMaterializerTargetLoader.LoadAsync(
                context, set, target, null, stagedProductIds, cancellationToken);
            return new SourceTarget(state);
        }
        catch (ConflictException)
        {
            return null;
        }
    }

    private static bool HasRequiredAttachment(
        RelatedTarget candidate,
        IReadOnlyCollection<ExistingAttachment> attachments) => attachments.Any(item =>
        item.ProductId == candidate.ProductId
        && item.Role == candidate.Role
        && (candidate.Role == OptionSetAttachmentRole.ProductChoice
            ? candidate.TargetId is null || item.CustomizationGroupId == candidate.TargetId
            : candidate.TargetId is null || item.MenuSectionId == candidate.TargetId));

    private static bool IsRequiredRequestTarget(
        RelatedTarget candidate,
        IReadOnlyDictionary<(Guid ProductId, OptionSetAttachmentRole Role), HashSet<Guid?>> requiredTargets) =>
        requiredTargets.TryGetValue((candidate.ProductId, candidate.Role), out var targetIds)
        && (candidate.TargetId is null || targetIds.Contains(candidate.TargetId));


}
