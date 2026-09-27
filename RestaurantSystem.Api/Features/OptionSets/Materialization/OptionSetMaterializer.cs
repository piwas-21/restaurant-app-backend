using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed partial class OptionSetMaterializer : IOptionSetMaterializer
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOptionSetCatalogService _catalog;
    private readonly ITenantFeatures _tenantFeatures;
    private readonly OptionSetAuthoringSettings _settings;

    public OptionSetMaterializer(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOptionSetCatalogService catalog,
        ITenantFeatures tenantFeatures,
        IOptions<OptionSetAuthoringSettings> settings)
    {
        _context = context;
        _currentUser = currentUser;
        _catalog = catalog;
        _tenantFeatures = tenantFeatures;
        _settings = settings.Value;
    }

    public async Task<OptionSetMaterializationPreview> PreviewAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
        => await PreviewAsync(request, null, cancellationToken);

    public async Task ValidateJobRequestAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        EnsureMaterializationEnabled();
        ValidateRequest(request, _settings.MaximumTargetsPerJob);
        await EnsureDifferencesHaveReasonsAsync(
            request, null, cancellationToken, _settings.MaximumTargetsPerJob);
    }

    public Task<OptionSetMaterializationTargetResultDto> ApplyJobTargetAsync(
        OptionSetMaterializationRequest request,
        OptionSetMaterializationTargetRequest target,
        OptionSetMaterializationJobTarget jobTarget,
        Guid leaseId,
        int? previousMenuVersion,
        string auditIdentifier,
        CancellationToken cancellationToken)
    {
        EnsureMaterializationEnabled();
        ValidateRequest(request, _settings.MaximumTargetsPerJob);
        var effectiveTarget = previousMenuVersion is int version
            ? WithExpectedMenuVersion(target, version)
            : target;
        return ApplyTargetAsync(
            effectiveTarget,
            new ApplyTargetContext(
                request, false, new Dictionary<Guid, int>(), new Dictionary<Guid, int>(),
                new OptionSetMaterializerValidationContext(null, _settings), cancellationToken,
                jobTarget, leaseId, auditIdentifier));
    }

    private async Task<OptionSetMaterializationPreview> PreviewAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken,
        int? maximumTargets = null)
    {
        ValidateRequest(request, maximumTargets);
        var set = await LoadExpectedSetAsync(request, cancellationToken);
        var preview = new OptionSetMaterializationPreview
        {
            OptionSetId = set.Id,
            SetVersion = set.Version
        };
        var validationContext = new OptionSetMaterializerValidationContext(
            stagedProductIds, _settings);

        foreach (var target in request.Targets)
        {
            preview.Targets.Add(await OptionSetMaterializerPlanBuilder.BuildAsync(
                _context, set, target, validationContext, cancellationToken));
        }

        preview.RelatedOfferWarnings.AddRange(await OptionSetRelatedOfferAnalyzer.AnalyzeAsync(
            _context, set, request, validationContext, cancellationToken));

        return preview;
    }

    public Task<CreateOrReuseImportedSetResult> CreateOrReuseImportedSetAsync(
        CreateOrReuseImportedSetRequest request,
        CancellationToken cancellationToken)
    {
        EnsureMaterializationEnabled();
        EnsureImportTransaction();
        return _catalog.CreateOrReuseImportedSetAsync(request, cancellationToken);
    }

    private void EnsureImportTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new ConflictException("Catalogue option-set imports must participate in an item transaction");
        }
    }

    private void EnsureMaterializationEnabled()
    {
        if (!_tenantFeatures.OptionSetMaterializationEnabled)
        {
            throw new OptionSetMaterializationDisabledException();
        }
    }

    private async Task<OptionSet> LoadExpectedSetAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var set = await _context.OptionSets.AsNoTracking().Include(item => item.Entries)
            .FirstOrDefaultAsync(item => item.Id == request.OptionSetId, cancellationToken)
            ?? throw new NotFoundException("Option set", request.OptionSetId);
        if (set.Version != request.ExpectedSetVersion)
        {
            throw new ConflictException("This option set changed. Reload it and preview the current version before applying.");
        }

        return set;
    }

    private void ValidateRequest(OptionSetMaterializationRequest request, int? maximumTargets = null)
    {
        var targetLimit = maximumTargets ?? _settings.MaximumTargetsPerRequest;
        if (request.OptionSetId == Guid.Empty || request.ExpectedSetVersion <= 0
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > _settings.MaximumIdempotencyKeyLength
            || request.Targets.Count is < 1 || request.Targets.Count > targetLimit)
        {
            throw new BadRequestException(
                $"A valid option set, version, idempotency key, and 1 to {targetLimit} targets are required");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var targetIds = new HashSet<(int Role, Guid ProductId, Guid? SectionId, Guid? CustomizationGroupId)>();
        foreach (var target in request.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.TargetKey)
                || !keys.Add(target.TargetKey.Trim()) || !Enum.IsDefined(target.Role)
                || !Enum.IsDefined(target.ConflictPolicy)
                || target.ExpectedAttachmentVersion is < 0
                || target.IntentionalDifferenceReason?.Trim().Length > _settings.MaximumIntentionalDifferenceReasonLength)
            {
                throw new BadRequestException("Targets need unique keys, valid roles and policies, and valid concurrency data");
            }

            if (!targetIds.Add(((int)target.Role, target.TargetProductId,
                target.TargetMenuSectionId, target.TargetCustomizationGroupId)))
            {
                throw new BadRequestException("A materialization request cannot repeat the same target and role");
            }

            if (target.EntryIds?.Count > _settings.MaximumEntriesPerOptionSet
                || target.Overrides?.Count > _settings.MaximumEntriesPerOptionSet)
            {
                throw new BadRequestException(
                    $"A target may select or override at most {_settings.MaximumEntriesPerOptionSet} entries");
            }
        }
    }

    private static OptionSetMaterializationTargetRequest WithExpectedMenuVersion(
        OptionSetMaterializationTargetRequest target,
        int version) => new()
        {
            TargetKey = target.TargetKey,
            Role = target.Role,
            TargetProductId = target.TargetProductId,
            TargetMenuSectionId = target.TargetMenuSectionId,
            TargetCustomizationGroupId = target.TargetCustomizationGroupId,
            ExpectedMenuAuthoringVersion = version,
            ExpectedCustomizationGroupVersion = target.ExpectedCustomizationGroupVersion,
            ExpectedAttachmentVersion = target.ExpectedAttachmentVersion,
            EntryIds = target.EntryIds,
            Overrides = target.Overrides,
            ConflictPolicy = target.ConflictPolicy,
            Settings = target.Settings,
            IntentionalDifferenceReason = target.IntentionalDifferenceReason
        };
}
