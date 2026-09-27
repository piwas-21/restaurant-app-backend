using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed partial class OptionSetMaterializationJobRunner : IOptionSetMaterializationJobRunner
{
    private const string ProcessingStatus = "processing";

    private readonly ApplicationDbContext _context;
    private readonly IOptionSetMaterializer _materializer;
    private readonly OptionSetAuthoringSettings _settings;
    private readonly ILogger<OptionSetMaterializationJobRunner> _logger;

    public OptionSetMaterializationJobRunner(
        ApplicationDbContext context,
        IOptionSetMaterializer materializer,
        IOptions<OptionSetAuthoringSettings> settings,
        ILogger<OptionSetMaterializationJobRunner> logger)
    {
        _context = context;
        _materializer = materializer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> RunNextBatchAsync(CancellationToken cancellationToken)
    {
        var claim = await TryClaimAsync(cancellationToken);
        if (claim is null)
        {
            return false;
        }

        JobInput input;
        try
        {
            input = await LoadRequestAsync(claim.JobId, cancellationToken);
            await ValidateBeforeRunAsync(claim, input, cancellationToken);
        }
        catch (Exception exception) when (exception is ConflictException or BadRequestException or NotFoundException or JsonException)
        {
            await MarkBlockedAsync(claim, exception.Message, cancellationToken);
            return true;
        }

        var targets = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
            .Where(target => target.JobId == claim.JobId && target.Status == "pending")
            .OrderBy(target => target.Sequence)
            .Take(_settings.TargetsPerJobRun)
            .ToListAsync(cancellationToken);
        var previousMenuVersions = await LoadPreviousMenuVersionsAsync(claim.JobId, targets, cancellationToken);

        foreach (var target in targets)
        {
            if (!await ProcessTargetAsync(
                claim, input.Request, input.CreatedBy, target, previousMenuVersions, cancellationToken))
            {
                break;
            }
        }

        await FinishBatchAsync(claim, cancellationToken);
        return true;
    }

    private async Task ValidateBeforeRunAsync(
        JobLease claim,
        JobInput input,
        CancellationToken cancellationToken)
    {
        if (!claim.IsFirstRun && !claim.RecoveredExpiredLease)
        {
            return;
        }

        var request = input.Request;
        if (claim.RecoveredExpiredLease)
        {
            var pendingSequences = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
                .Where(target => target.JobId == claim.JobId && target.Status == "pending")
                .OrderBy(target => target.Sequence)
                .Select(target => target.Sequence)
                .ToListAsync(cancellationToken);
            if (pendingSequences.Count == 0)
            {
                return;
            }

            request = OptionSetMaterializationJobJson.WithTargets(request, pendingSequences);
        }

        await _materializer.ValidateJobRequestAsync(request, cancellationToken);
    }

    private async Task<bool> ProcessTargetAsync(
        JobLease claim,
        OptionSetMaterializationRequest request,
        string auditIdentifier,
        OptionSetMaterializationJobTarget target,
        Dictionary<Guid, List<PreviousMenuVersion>> previousMenuVersions,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        try
        {
            var targetRequest = OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationTargetRequest>(
                target.RequestJson);
            if (target.Sequence >= request.Targets.Count
                || targetRequest.TargetKey != request.Targets[target.Sequence].TargetKey
                || targetRequest.TargetProductId != request.Targets[target.Sequence].TargetProductId)
            {
                await MarkBlockedAsync(claim, "The saved job target no longer matches its immutable request.", cancellationToken);
                return false;
            }

            var previousMenuVersion = GetPreviousMenuVersion(target, targetRequest, previousMenuVersions);
            var result = await _materializer.ApplyJobTargetAsync(
                request, targetRequest, target, claim.LeaseId, previousMenuVersion, auditIdentifier,
                cancellationToken);
            if (result.Status == "conflict")
            {
                return await PersistOutcomeAsync(claim, target.Id, result, "conflict", null, null, cancellationToken);
            }

            RememberMenuVersion(target, targetRequest, result, previousMenuVersions);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BadRequestException exception)
        {
            var result = ConflictResult(target.TargetKey, "invalid-target", exception.Message);
            return await PersistOutcomeAsync(
                claim, target.Id, result, "conflict", "invalid-target", exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Option-set materialization target {TargetId} failed.", target.Id);
            return await PersistOutcomeAsync(
                claim, target.Id, null, "failed", "target-processing-failed",
                "Target processing failed. Resume this job to retry it if the issue is transient.",
                cancellationToken);
        }
    }

    private async Task<JobLease?> TryClaimAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var candidateId = await _context.OptionSetMaterializationJobs.AsNoTracking()
            .Where(job => job.Status == "queued"
                || job.Status == ProcessingStatus && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now))
            .OrderBy(job => job.CreatedAt)
            .Select(job => new { job.Id, job.Status, job.StartedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidateId is null)
        {
            return null;
        }

        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.AddSeconds(_settings.JobLeaseSeconds);
        var claimed = await _context.OptionSetMaterializationJobs
            .Where(job => job.Id == candidateId.Id
                && (job.Status == "queued"
                    || job.Status == ProcessingStatus && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(job => job.Status, ProcessingStatus)
                .SetProperty(job => job.LeaseId, leaseId)
                .SetProperty(job => job.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(job => job.StartedAt, job => job.StartedAt ?? now)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
        return claimed == 1
            ? new JobLease(
                candidateId.Id, leaseId, candidateId.Status == ProcessingStatus, candidateId.StartedAt is null)
            : null;
    }

    private async Task<JobInput> LoadRequestAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _context.OptionSetMaterializationJobs.AsNoTracking()
            .SingleAsync(row => row.Id == jobId, cancellationToken);
        var request = OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationRequest>(job.RequestJson);
        request.OptionSetId = job.OptionSetId;
        return new JobInput(request, job.CreatedBy);
    }

    private async Task<Dictionary<Guid, List<PreviousMenuVersion>>> LoadPreviousMenuVersionsAsync(
        Guid jobId,
        IReadOnlyCollection<OptionSetMaterializationJobTarget> targets,
        CancellationToken cancellationToken)
    {
        var productIds = targets.Select(target => target.TargetProductId).Distinct().ToArray();
        if (productIds.Length == 0)
        {
            return [];
        }

        var lastSequence = targets.Max(target => target.Sequence);
        var previous = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
            .Where(row => row.JobId == jobId && productIds.Contains(row.TargetProductId)
                && row.Sequence < lastSequence && (row.Status == "applied" || row.Status == "unchanged")
                && row.ResultJson != null
                && EF.Functions.JsonContains(row.RequestJson, "{\"role\":\"bundleChoice\"}"))
            .OrderBy(row => row.Sequence)
            .Select(row => new PreviousMenuVersion(row.TargetProductId, row.Sequence, row.ResultJson, null))
            .ToListAsync(cancellationToken);

        return previous.GroupBy(row => row.TargetProductId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static int? GetPreviousMenuVersion(
        OptionSetMaterializationJobTarget target,
        OptionSetMaterializationTargetRequest request,
        IReadOnlyDictionary<Guid, List<PreviousMenuVersion>> previousMenuVersions)
    {
        if (request.Role != OptionSetAttachmentRole.BundleChoice
            || !previousMenuVersions.TryGetValue(target.TargetProductId, out var previous))
        {
            return null;
        }

        var latest = previous.LastOrDefault(row => row.Sequence < target.Sequence);
        if (latest is null)
        {
            return null;
        }

        return latest.ResultJson is null
            ? latest.MenuAuthoringVersion
            : OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationTargetResultDto>(latest.ResultJson)
                .MenuAuthoringVersion;
    }

    private static void RememberMenuVersion(
        OptionSetMaterializationJobTarget target,
        OptionSetMaterializationTargetRequest request,
        OptionSetMaterializationTargetResultDto result,
        Dictionary<Guid, List<PreviousMenuVersion>> previousMenuVersions)
    {
        if (request.Role != OptionSetAttachmentRole.BundleChoice
            || result.Status is not ("applied" or "unchanged"))
        {
            return;
        }

        if (!previousMenuVersions.TryGetValue(target.TargetProductId, out var versions))
        {
            versions = [];
            previousMenuVersions[target.TargetProductId] = versions;
        }

        versions.Add(new PreviousMenuVersion(
            target.TargetProductId, target.Sequence, null, result.MenuAuthoringVersion));
    }

    private sealed record JobLease(Guid JobId, Guid LeaseId, bool RecoveredExpiredLease, bool IsFirstRun);
    private sealed record JobInput(OptionSetMaterializationRequest Request, string CreatedBy);
    private sealed record PreviousMenuVersion(
        Guid TargetProductId,
        int Sequence,
        string? ResultJson,
        int? MenuAuthoringVersion);
}
