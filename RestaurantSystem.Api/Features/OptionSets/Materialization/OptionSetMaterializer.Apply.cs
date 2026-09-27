using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed partial class OptionSetMaterializer
{
    public async Task<OptionSetMaterializationResult> ApplyAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        EnsureMaterializationEnabled();
        return await ApplyCoreAsync(request, null, cancellationToken);
    }

    public async Task<OptionSetMaterializationResult> ApplyImportedAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid> stagedProductIds,
        CancellationToken cancellationToken)
    {
        EnsureMaterializationEnabled();
        EnsureImportTransaction();
        if (stagedProductIds is null || stagedProductIds.Contains(Guid.Empty))
        {
            throw new BadRequestException("The import staged-product allowlist is invalid");
        }

        return await ApplyCoreAsync(request, stagedProductIds, cancellationToken);
    }

    private async Task<OptionSetMaterializationResult> ApplyCoreAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await EnsureDifferencesHaveReasonsAsync(request, stagedProductIds, cancellationToken);
        var ambientTransaction = _context.Database.CurrentTransaction is not null;
        var result = new OptionSetMaterializationResult
        {
            OptionSetId = request.OptionSetId,
            SetVersion = request.ExpectedSetVersion
        };
        var validationContext = new OptionSetMaterializerValidationContext(
            stagedProductIds, _settings);
        var menuVersionBases = new Dictionary<Guid, int>();
        var menuVersionAdvances = new Dictionary<Guid, int>();

        foreach (var target in request.Targets)
        {
            result.Targets.Add(await ApplyTargetAsync(
                request, target, ambientTransaction, menuVersionBases, menuVersionAdvances,
                validationContext, cancellationToken));
        }

        return result;
    }

    private async Task<OptionSetMaterializationTargetResultDto> ApplyTargetAsync(
        OptionSetMaterializationRequest request,
        OptionSetMaterializationTargetRequest target,
        bool ambientTransaction,
        IDictionary<Guid, int> menuVersionBases,
        IDictionary<Guid, int> menuVersionAdvances,
        OptionSetMaterializerValidationContext validationContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = ambientTransaction
            ? null
            : await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var set = await LoadExpectedSetAsync(request, cancellationToken);
            var effectiveTarget = WithEffectiveMenuVersion(target, menuVersionBases, menuVersionAdvances);
            var targetResult = await OptionSetMaterializerTargetWriter.ApplyAsync(
                _context,
                set,
                effectiveTarget,
                request.IdempotencyKey.Trim(),
                _currentUser.GetAuditIdentifier(),
                validationContext,
                cancellationToken);

            await EnsureSetVersionUnchangedAsync(request, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            AdvanceMenuVersion(target, targetResult, menuVersionBases, menuVersionAdvances);
            return targetResult;
        }
        catch (ConflictException exception) when (!ambientTransaction)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return ConflictResult(target, "stale-or-invalid-target", exception.Message);
        }
        catch (DbUpdateConcurrencyException) when (!ambientTransaction)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return ConflictResult(target, "concurrent-update", "The target changed during apply. Reload it and review the diff.");
        }
        catch (DbUpdateException) when (!ambientTransaction)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return ConflictResult(target, "stable-reference-conflict", "A canonical option or target row changed concurrently. Reload the target and review the diff.");
        }
        catch (PostgresException exception) when (!ambientTransaction && exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await RollbackAndClearAsync(transaction, cancellationToken);
            return ConflictResult(target, "concurrent-update", "The target changed during apply. Reload it and review the diff.");
        }
    }

    private async Task EnsureSetVersionUnchangedAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var currentVersion = await _context.OptionSets.AsNoTracking()
            .Where(set => set.Id == request.OptionSetId)
            .Select(set => (int?)set.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentVersion != request.ExpectedSetVersion)
        {
            throw new ConflictException("The option set changed while applying. Reload it and preview again.");
        }
    }

    private static OptionSetMaterializationTargetRequest WithEffectiveMenuVersion(
        OptionSetMaterializationTargetRequest target,
        IDictionary<Guid, int> versionBases,
        IDictionary<Guid, int> versionAdvances)
    {
        if (target.ExpectedMenuAuthoringVersion is not int requestedVersion)
        {
            return target;
        }

        if (!versionBases.TryGetValue(target.TargetProductId, out var baseVersion))
        {
            versionBases[target.TargetProductId] = requestedVersion;
            baseVersion = requestedVersion;
        }

        if (baseVersion != requestedVersion)
        {
            throw new ConflictException("Targets for one menu must use the same starting authoring version");
        }

        return new OptionSetMaterializationTargetRequest
        {
            TargetKey = target.TargetKey,
            Role = target.Role,
            TargetProductId = target.TargetProductId,
            TargetMenuSectionId = target.TargetMenuSectionId,
            TargetCustomizationGroupId = target.TargetCustomizationGroupId,
            ExpectedMenuAuthoringVersion = baseVersion + GetValue(versionAdvances, target.TargetProductId),
            ExpectedCustomizationGroupVersion = target.ExpectedCustomizationGroupVersion,
            ExpectedAttachmentVersion = target.ExpectedAttachmentVersion,
            EntryIds = target.EntryIds,
            Overrides = target.Overrides,
            ConflictPolicy = target.ConflictPolicy,
            Settings = target.Settings,
            IntentionalDifferenceReason = target.IntentionalDifferenceReason
        };
    }

    private static void AdvanceMenuVersion(
        OptionSetMaterializationTargetRequest target,
        OptionSetMaterializationTargetResultDto result,
        IDictionary<Guid, int> versionBases,
        IDictionary<Guid, int> versionAdvances)
    {
        if (result.MenuAuthoringVersion is not int current)
        {
            return;
        }

        var baseVersion = target.ExpectedMenuAuthoringVersion ?? current;
        versionBases.TryAdd(target.TargetProductId, baseVersion);
        baseVersion = versionBases[target.TargetProductId];
        versionAdvances[target.TargetProductId] = Math.Max(0, current - baseVersion);
    }

    private static int GetValue(IDictionary<Guid, int> values, Guid key) =>
        values.TryGetValue(key, out var value) ? value : 0;

    private async Task RollbackAndClearAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        _context.ChangeTracker.Clear();
    }

    private static OptionSetMaterializationTargetResultDto ConflictResult(
        OptionSetMaterializationTargetRequest target,
        string code,
        string message) => new()
        {
            TargetKey = target.TargetKey,
            Status = "conflict",
            Conflicts = [new OptionSetMaterializationConflictDto { Code = code, Message = message }]
        };

    private async Task EnsureDifferencesHaveReasonsAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(request, stagedProductIds, cancellationToken);
        var targetByKey = request.Targets.ToDictionary(target => target.TargetKey, StringComparer.Ordinal);
        foreach (var warning in preview.RelatedOfferWarnings.Where(warning => warning.ReasonRequired))
        {
            var target = targetByKey[warning.TargetKey];
            if (string.IsNullOrWhiteSpace(target.IntentionalDifferenceReason))
            {
                throw new ConflictException(
                    $"The linked offer '{warning.RelatedProductName}' lacks this required choice. Include it or record an intentional difference reason.");
            }
        }
    }
}
