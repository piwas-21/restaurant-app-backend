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
            await _materializer.ValidateJobRequestAsync(input.Request, cancellationToken);
        }
        catch (Exception exception) when (exception is ConflictException or BadRequestException or NotFoundException or JsonException)
        {
            await MarkBlockedAsync(claim, exception.Message, cancellationToken);
            return true;
        }

        var targetIds = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
            .Where(target => target.JobId == claim.JobId && target.Status == "pending")
            .OrderBy(target => target.Sequence)
            .Select(target => target.Id)
            .Take(_settings.TargetsPerJobRun)
            .ToListAsync(cancellationToken);

        foreach (var targetId in targetIds)
        {
            if (!await ProcessTargetAsync(claim, input.Request, input.CreatedBy, targetId, cancellationToken))
            {
                break;
            }
        }

        await FinishBatchAsync(claim, cancellationToken);
        return true;
    }

    private async Task<bool> ProcessTargetAsync(
        JobLease claim,
        OptionSetMaterializationRequest request,
        string auditIdentifier,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        try
        {
            var target = await _context.OptionSetMaterializationJobTargets
                .SingleAsync(row => row.Id == targetId && row.JobId == claim.JobId, cancellationToken);
            var targetRequest = OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationTargetRequest>(
                target.RequestJson);
            if (target.Sequence >= request.Targets.Count
                || targetRequest.TargetKey != request.Targets[target.Sequence].TargetKey
                || targetRequest.TargetProductId != request.Targets[target.Sequence].TargetProductId)
            {
                await MarkBlockedAsync(claim, "The saved job target no longer matches its immutable request.", cancellationToken);
                return false;
            }

            var previousMenuVersion = await GetPreviousMenuVersionAsync(target, targetRequest, cancellationToken);
            var result = await _materializer.ApplyJobTargetAsync(
                request, targetRequest, target, claim.LeaseId, previousMenuVersion, auditIdentifier,
                cancellationToken);
            if (result.Status == "conflict")
            {
                return await PersistOutcomeAsync(claim, targetId, result, "conflict", null, null, cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BadRequestException exception)
        {
            _context.ChangeTracker.Clear();
            var targetKey = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
                .Where(row => row.Id == targetId)
                .Select(row => row.TargetKey)
                .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            var result = ConflictResult(targetKey, "invalid-target", exception.Message);
            return await PersistOutcomeAsync(
                claim, targetId, result, "conflict", "invalid-target", exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Option-set materialization target {TargetId} failed.", targetId);
            return await PersistOutcomeAsync(
                claim, targetId, null, "failed", "target-processing-failed",
                "Target processing failed. Resume this job to retry it if the issue is transient.",
                cancellationToken);
        }
    }

    private async Task<JobLease?> TryClaimAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var candidateId = await _context.OptionSetMaterializationJobs.AsNoTracking()
            .Where(job => job.Status == "queued"
                || job.Status == "processing" && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now))
            .OrderBy(job => job.CreatedAt)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidateId is null)
        {
            return null;
        }

        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.AddSeconds(_settings.JobLeaseSeconds);
        var claimed = await _context.OptionSetMaterializationJobs
            .Where(job => job.Id == candidateId.Value
                && (job.Status == "queued"
                    || job.Status == "processing" && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(job => job.Status, "processing")
                .SetProperty(job => job.LeaseId, leaseId)
                .SetProperty(job => job.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(job => job.StartedAt, job => job.StartedAt ?? now)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
        return claimed == 1 ? new JobLease(candidateId.Value, leaseId) : null;
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

    private async Task<int?> GetPreviousMenuVersionAsync(
        OptionSetMaterializationJobTarget target,
        OptionSetMaterializationTargetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Role != OptionSetAttachmentRole.BundleChoice)
        {
            return null;
        }

        var previousJson = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
            .Where(row => row.JobId == target.JobId && row.TargetProductId == target.TargetProductId
                && row.Sequence < target.Sequence && (row.Status == "applied" || row.Status == "unchanged"))
            .OrderByDescending(row => row.Sequence)
            .Select(row => row.ResultJson)
            .FirstOrDefaultAsync(cancellationToken);
        return previousJson is null
            ? null
            : OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationTargetResultDto>(previousJson)
                .MenuAuthoringVersion;
    }

    private sealed record JobLease(Guid JobId, Guid LeaseId);
    private sealed record JobInput(OptionSetMaterializationRequest Request, string CreatedBy);
}
