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

    private async Task<OptionSetMaterializationPreview> PreviewAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var set = await LoadExpectedSetAsync(request, cancellationToken);
        var preview = new OptionSetMaterializationPreview
        {
            OptionSetId = set.Id,
            SetVersion = set.Version
        };
        var validationContext = new OptionSetMaterializerValidationContext(
            stagedProductIds, _settings.MaximumEntriesPerOptionSet);

        foreach (var target in request.Targets)
        {
            preview.Targets.Add(await OptionSetMaterializerPlanBuilder.BuildAsync(
                _context, set, target, validationContext, cancellationToken));
        }

        preview.RelatedOfferWarnings.AddRange(await OptionSetRelatedOfferAnalyzer.AnalyzeAsync(
            _context, set, request, stagedProductIds, cancellationToken));

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

    private void ValidateRequest(OptionSetMaterializationRequest request)
    {
        if (request.OptionSetId == Guid.Empty || request.ExpectedSetVersion <= 0
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > _settings.MaximumIdempotencyKeyLength
            || request.Targets.Count is < 1 || request.Targets.Count > _settings.MaximumTargetsPerRequest)
        {
            throw new BadRequestException(
                $"A valid option set, version, idempotency key, and 1 to {_settings.MaximumTargetsPerRequest} targets are required");
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
}
