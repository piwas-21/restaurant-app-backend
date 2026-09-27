using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed partial class OptionSetMaterializationJobRunner
{
    private async Task<bool> PersistOutcomeAsync(
        JobLease claim,
        Guid targetId,
        OptionSetMaterializationTargetResultDto? result,
        string status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (!await RenewLeaseAsync(claim, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var now = DateTime.UtcNow;
        var resultJson = result is null ? null : OptionSetMaterializationJobJson.Serialize(result);
        var safeError = Truncate(errorMessage, OptionSetMaterializationLimits.PersistedErrorMaxLength);
        var updated = await _context.OptionSetMaterializationJobTargets
            .Where(row => row.Id == targetId && row.JobId == claim.JobId && row.Status == "pending")
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.Attempts, row => row.Attempts + 1)
                .SetProperty(row => row.ResultJson, resultJson)
                .SetProperty(row => row.ErrorCode, errorCode)
                .SetProperty(row => row.ErrorMessage, safeError)
                .SetProperty(row => row.CompletedAt, status == "failed" ? (DateTime?)null : now)
                .SetProperty(row => row.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> RenewLeaseAsync(JobLease claim, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await _context.OptionSetMaterializationJobs
            .Where(job => job.Id == claim.JobId && job.LeaseId == claim.LeaseId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(job => job.LeaseExpiresAt, now.AddSeconds(_settings.JobLeaseSeconds))
                .SetProperty(job => job.UpdatedAt, now), cancellationToken) == 1;
    }

    private async Task MarkBlockedAsync(JobLease claim, string error, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (!await RenewLeaseAsync(claim, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var job = await _context.OptionSetMaterializationJobs
            .Include(row => row.Targets)
            .SingleAsync(row => row.Id == claim.JobId, cancellationToken);
        var now = DateTime.UtcNow;
        var safeError = Truncate(error, OptionSetMaterializationLimits.PersistedErrorMaxLength)!;
        job.Status = "blocked";
        job.LastError = safeError;
        job.CompletedAt = now;
        job.LeaseId = null;
        job.LeaseExpiresAt = null;
        job.UpdatedAt = now;
        foreach (var target in job.Targets.Where(row => row.Status == "pending"))
        {
            target.Status = "conflict";
            target.Attempts++;
            target.ResultJson = OptionSetMaterializationJobJson.Serialize(
                ConflictResult(target.TargetKey, "batch-preflight-conflict", safeError));
            target.ErrorCode = "batch-preflight-conflict";
            target.ErrorMessage = safeError;
            target.CompletedAt = now;
            target.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FinishBatchAsync(JobLease claim, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (!await RenewLeaseAsync(claim, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var statuses = await _context.OptionSetMaterializationJobTargets.AsNoTracking()
            .Where(target => target.JobId == claim.JobId)
            .Select(target => target.Status)
            .ToListAsync(cancellationToken);
        var hasPending = statuses.Contains("pending");
        var hasErrors = statuses.Contains("failed") || statuses.Contains("conflict");
        string status;
        if (hasPending)
        {
            status = "queued";
        }
        else if (hasErrors)
        {
            status = "partial";
        }
        else
        {
            status = "completed";
        }
        var now = DateTime.UtcNow;
        await _context.OptionSetMaterializationJobs
            .Where(job => job.Id == claim.JobId && job.LeaseId == claim.LeaseId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(job => job.Status, status)
                .SetProperty(job => job.LastError, hasErrors ? "One or more targets need review or retry." : null)
                .SetProperty(job => job.CompletedAt, hasPending ? (DateTime?)null : now)
                .SetProperty(job => job.LeaseId, (Guid?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) => value is null || value.Length <= maxLength
        ? value
        : value[..maxLength];

    private static OptionSetMaterializationTargetResultDto ConflictResult(
        string targetKey,
        string code,
        string message) => new()
        {
            TargetKey = targetKey,
            Status = "conflict",
            Conflicts = [new OptionSetMaterializationConflictDto { Code = code, Message = message }]
        };
}
