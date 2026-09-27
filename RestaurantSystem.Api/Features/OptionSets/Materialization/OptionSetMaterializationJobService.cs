using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationJobService : IOptionSetMaterializationJobService
{
    private readonly ApplicationDbContext _context;
    private readonly IOptionSetMaterializer _materializer;
    private readonly ITenantFeatures _features;
    private readonly OptionSetAuthoringSettings _settings;
    private readonly ICurrentUserService _currentUser;

    public OptionSetMaterializationJobService(
        ApplicationDbContext context,
        IOptionSetMaterializer materializer,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOptions<OptionSetAuthoringSettings> settings)
    {
        _context = context;
        _materializer = materializer;
        _features = features;
        _currentUser = currentUser;
        _settings = settings.Value;
    }

    public async Task<OptionSetMaterializationJobDto> CreateAsync(
        Guid optionSetId,
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_features.OptionSetMaterializationEnabled)
        {
            throw new OptionSetMaterializationDisabledException();
        }

        request.OptionSetId = optionSetId;
        request.IdempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        NormalizeTargetKeys(request.Targets);
        var fingerprint = OptionSetMaterializationJobJson.Fingerprint(request);
        var existing = await FindByKeyAsync(optionSetId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return MatchExisting(existing, fingerprint);
        }

        ValidateLargeRequest(request);
        await _materializer.ValidateJobRequestAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        var job = new OptionSetMaterializationJob
        {
            Id = Guid.NewGuid(),
            OptionSetId = optionSetId,
            SetVersion = request.ExpectedSetVersion,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = fingerprint,
            RequestJson = OptionSetMaterializationJobJson.Serialize(request),
            Status = "queued",
            CreatedBy = _currentUser.GetAuditIdentifier(),
            CreatedAt = now,
            UpdatedAt = now
        };
        job.Targets = request.Targets.Select((target, sequence) => new OptionSetMaterializationJobTarget
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            TargetKey = target.TargetKey.Trim(),
            TargetProductId = target.TargetProductId,
            RequestJson = OptionSetMaterializationJobJson.Serialize(target),
            Status = "pending",
            CreatedBy = _currentUser.GetAuditIdentifier(),
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _context.OptionSetMaterializationJobs.Add(job);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            existing = await FindByKeyAsync(optionSetId, request.IdempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return MatchExisting(existing, fingerprint);
        }

        return ToDto(job);
    }

    public async Task<OptionSetMaterializationJobDto> GetAsync(
        Guid optionSetId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _context.OptionSetMaterializationJobs.AsNoTracking()
            .Include(item => item.Targets.OrderBy(target => target.Sequence))
            .FirstOrDefaultAsync(item => item.Id == jobId && item.OptionSetId == optionSetId, cancellationToken)
            ?? throw new NotFoundException("Option-set materialization job was not found");
        return ToDto(job);
    }

    public async Task<OptionSetMaterializationJobDto> ResumeAsync(
        Guid optionSetId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!_features.OptionSetMaterializationEnabled)
        {
            throw new OptionSetMaterializationDisabledException();
        }

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var released = await _context.OptionSetMaterializationJobs
            .Where(job => job.Id == jobId && job.OptionSetId == optionSetId
                && job.Status != "completed" && job.Status != "blocked"
                && (job.Status != "processing" || job.LeaseExpiresAt == null || job.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(job => job.Status, "queued")
                .SetProperty(job => job.LeaseId, (Guid?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(job => job.LastError, (string?)null)
                .SetProperty(job => job.CompletedAt, (DateTime?)null)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
        if (released != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            var exists = await _context.OptionSetMaterializationJobs.AsNoTracking()
                .AnyAsync(job => job.Id == jobId && job.OptionSetId == optionSetId, cancellationToken);
            if (!exists)
            {
                throw new NotFoundException("Option-set materialization job was not found");
            }

            var current = await _context.OptionSetMaterializationJobs.AsNoTracking()
                .Where(job => job.Id == jobId && job.OptionSetId == optionSetId)
                .Select(job => job.Status)
                .SingleAsync(cancellationToken);
            if (current is "completed" or "blocked")
            {
                throw new ConflictException("This job cannot be resumed; review it and create a new request if changes are needed.");
            }

            throw new ConflictException("This option-set job is still being processed. Poll its status before resuming.");
        }

        var job = await LoadAsync(optionSetId, jobId, cancellationToken);
        var retryable = job.Targets.Where(target => target.Status == "failed").ToArray();
        var pending = job.Targets.Any(target => target.Status == "pending");
        if (retryable.Length == 0 && !pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("This job has no pending or retryable targets; review conflicts and create a new job.");
        }

        var request = OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationRequest>(job.RequestJson);
        request.OptionSetId = job.OptionSetId;
        var targetsToValidate = job.Targets
            .Where(target => target.Status is "pending" or "failed")
            .Select(target => target.Sequence);
        await _materializer.ValidateJobRequestAsync(
            OptionSetMaterializationJobJson.WithTargets(request, targetsToValidate), cancellationToken);

        foreach (var target in retryable)
        {
            target.Status = "pending";
            target.ErrorCode = null;
            target.ErrorMessage = null;
            target.CompletedAt = null;
            target.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(job);
    }

    private void ValidateLargeRequest(OptionSetMaterializationRequest request)
    {
        if (request.OptionSetId == Guid.Empty || request.ExpectedSetVersion <= 0
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > _settings.MaximumIdempotencyKeyLength
            || request.Targets is null
            || request.Targets.Count <= _settings.MaximumTargetsPerRequest
            || request.Targets.Count > _settings.MaximumTargetsPerJob)
        {
            throw new BadRequestException(
                $"Apply jobs require {_settings.MaximumTargetsPerRequest + 1} to {_settings.MaximumTargetsPerJob} targets, a valid option set/version, and an idempotency key.");
        }
    }

    private static void NormalizeTargetKeys(IReadOnlyList<OptionSetMaterializationTargetRequest>? targets)
    {
        if (targets is null)
        {
            return;
        }

        foreach (var target in targets)
        {
            if (target is not null)
            {
                target.TargetKey = target.TargetKey?.Trim() ?? string.Empty;
            }
        }
    }

    private Task<OptionSetMaterializationJob?> FindByKeyAsync(
        Guid optionSetId,
        string idempotencyKey,
        CancellationToken cancellationToken) => _context.OptionSetMaterializationJobs
        .AsNoTracking()
        .Include(job => job.Targets)
        .FirstOrDefaultAsync(job => job.OptionSetId == optionSetId
            && job.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<OptionSetMaterializationJob> LoadAsync(
        Guid optionSetId,
        Guid jobId,
        CancellationToken cancellationToken) => await _context.OptionSetMaterializationJobs
        .Include(job => job.Targets.OrderBy(target => target.Sequence))
        .FirstOrDefaultAsync(job => job.Id == jobId && job.OptionSetId == optionSetId, cancellationToken)
        ?? throw new NotFoundException("Option-set materialization job was not found");

    private static OptionSetMaterializationJobDto MatchExisting(
        OptionSetMaterializationJob existing,
        string fingerprint)
    {
        if (!string.Equals(existing.RequestHash, fingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException("This idempotency key already belongs to a different option-set apply request.");
        }

        return ToDto(existing);
    }

    private static OptionSetMaterializationJobDto ToDto(OptionSetMaterializationJob job) => new()
    {
        JobId = job.Id,
        OptionSetId = job.OptionSetId,
        SetVersion = job.SetVersion,
        Status = job.Status,
        CreatedAt = job.CreatedAt,
        CompletedAt = job.CompletedAt,
        LastError = job.LastError,
        Targets = job.Targets.OrderBy(target => target.Sequence).Select(ToDto).ToList()
    };

    private static OptionSetMaterializationJobTargetDto ToDto(OptionSetMaterializationJobTarget target) => new()
    {
        Sequence = target.Sequence,
        TargetKey = target.TargetKey,
        TargetProductId = target.TargetProductId,
        Status = target.Status,
        Attempts = target.Attempts,
        Result = DeserializeResult(target.ResultJson),
        ErrorCode = target.ErrorCode,
        ErrorMessage = target.ErrorMessage
    };

    private static OptionSetMaterializationTargetResultDto? DeserializeResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationTargetResultDto>(json);
        }
        catch (JsonException exception)
        {
            throw new ConflictException("A saved target result is invalid; the job cannot be displayed safely.", exception);
        }
    }
}
