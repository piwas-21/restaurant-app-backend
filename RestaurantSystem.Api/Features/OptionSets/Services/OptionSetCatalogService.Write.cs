using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService
{
    public async Task<OptionSetDetailDto> CreateAsync(OptionSetWriteRequestDto request, CancellationToken cancellationToken)
    {
        ValidateHeader(request, creating: true);
        ValidateEntryCount(request);
        await ValidateEntriesAsync(request.Kind, request.Entries, cancellationToken);
        var name = request.Name.Trim();
        var normalizedName = OptionSetNameNormalizer.Normalize(name);
        if (await _context.OptionSets.AnyAsync(set => set.Kind == request.Kind && set.NormalizedName == normalizedName, cancellationToken))
        {
            throw new ConflictException("An option set with this name and kind already exists");
        }

        var now = DateTime.UtcNow;
        var audit = _currentUser.GetAuditIdentifier();
        var translations = OptionSetLocales.NormalizeTranslations(request.Translations, _settings);
        var set = new OptionSet
        {
            Id = Guid.NewGuid(),
            Kind = request.Kind,
            Name = name,
            SourceLocale = OptionSetLocales.NormalizeLocale(request.SourceLocale, _settings.MaximumLocaleTagLength),
            NormalizedName = normalizedName,
            Status = OptionSetStatus.Active,
            Version = 1,
            CreatedAt = now,
            CreatedBy = audit,
            Entries = request.Entries.Select(entry => NewEntry(entry, audit, now)).ToList()
        };
        set.Translations = OptionSetLocales.CreateEntities(set, translations, audit, now);
        await _context.OptionSets.AddAsync(set, cancellationToken);
        await RecordTranslationAsync(set, request, cancellationToken);
        await SaveWithConflictTranslationAsync(cancellationToken);
        return ToDetail(set);
    }

    public async Task<OptionSetDetailDto> UpdateAsync(
        Guid id,
        int expectedVersion,
        OptionSetWriteRequestDto request,
        CancellationToken cancellationToken)
    {
        ValidateHeader(request, creating: false);
        if (expectedVersion <= 0)
        {
            throw new BadRequestException("A positive If-Match option-set version is required");
        }

        var set = await _context.OptionSets.AsSplitQuery().Include(item => item.Entries).Include(item => item.Attachments)
            .Include(item => item.Translations)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Option set", id);
        if (set.Version != expectedVersion)
        {
            throw new ConflictException("This option set changed. Reload it and review the current values.");
        }

        if (set.Kind != request.Kind)
        {
            throw new BadRequestException("An option set cannot change kind after creation");
        }

        ValidateEntryCount(request);
        var now = DateTime.UtcNow;
        var audit = _currentUser.GetAuditIdentifier();
        var plan = BuildUpdatePlan(request, set);
        await ValidateEntriesAsync(request.Kind, plan.EntriesToValidate, cancellationToken);
        var materializedEntryIds = await LoadMaterializedEntryIdsAsync(set, cancellationToken);
        ApplyPlannedEntries(set, plan, materializedEntryIds, now, audit);
        DisableRemovedEntries(set, plan.RetainedIds, now, audit);
        UpdateSetMetadata(set, request, audit, now);
        await RecordTranslationAsync(set, request, cancellationToken);
        await SaveWithConflictTranslationAsync(cancellationToken);
        return ToDetail(set);
    }

    private static UpdatePlan BuildUpdatePlan(OptionSetWriteRequestDto request, OptionSet set)
    {
        var byId = set.Entries.ToDictionary(entry => entry.Id);
        var retained = new HashSet<Guid>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var plannedEntries = new List<(OptionSetEntryDto Dto, OptionSetEntry? Existing)>();
        var entriesToValidate = new List<OptionSetEntryDto>();

        foreach (var dto in request.Entries)
        {
            var reference = CanonicalReference(dto);
            if (!references.Add(reference))
            {
                throw new BadRequestException("An option set cannot contain the same canonical item twice");
            }

            var entry = ResolveExistingEntry(dto, set.Entries, byId, retained);
            if (entry is null || !SameReference(entry, dto))
            {
                entriesToValidate.Add(dto);
            }

            plannedEntries.Add((dto, entry));
        }

        return new UpdatePlan(plannedEntries, entriesToValidate, retained);
    }

    private async Task<HashSet<Guid>> LoadMaterializedEntryIdsAsync(
        OptionSet set,
        CancellationToken cancellationToken)
    {
        var entryIds = set.Entries.Select(entry => entry.Id).ToArray();
        return entryIds.Length == 0
            ? []
            : (await _context.OptionSetAppliedRows.Where(row => entryIds.Contains(row.OptionSetEntryId))
                .Select(row => row.OptionSetEntryId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }

    private void ApplyPlannedEntries(
        OptionSet set,
        UpdatePlan plan,
        HashSet<Guid> materializedEntryIds,
        DateTime now,
        string audit)
    {
        foreach (var (dto, existingEntry) in plan.Entries)
        {
            var entry = existingEntry;
            if (entry is null)
            {
                entry = NewEntry(dto, audit, now);
                entry.OptionSetId = set.Id;
                entry.OptionSet = set;
                set.Entries.Add(entry);
                _context.OptionSetEntries.Add(entry);
                plan.RetainedIds.Add(entry.Id);
            }
            else
            {
                if (materializedEntryIds.Contains(entry.Id) && !SameReference(entry, dto))
                {
                    throw new ConflictException("Detach this entry from its targets before changing its canonical item");
                }

                CopyEntry(dto, entry, audit, now);
                entry.IsEnabled = true;
                plan.RetainedIds.Add(entry.Id);
            }
        }
    }

    private static void DisableRemovedEntries(OptionSet set, HashSet<Guid> retainedIds, DateTime now, string audit)
    {
        var disabledEntries = set.Entries.Where(entry => entry.IsEnabled && !retainedIds.Contains(entry.Id)).ToList();
        foreach (var entry in disabledEntries)
        {
            entry.IsEnabled = false;
            entry.UpdatedAt = now;
            entry.UpdatedBy = audit;
        }
    }

    private void UpdateSetMetadata(OptionSet set, OptionSetWriteRequestDto request, string audit, DateTime now)
    {
        set.Name = request.Name.Trim();
        set.SourceLocale = OptionSetLocales.NormalizeLocale(request.SourceLocale, _settings.MaximumLocaleTagLength);
        set.NormalizedName = OptionSetNameNormalizer.Normalize(set.Name);
        OptionSetLocales.Sync(
            set.Translations,
            OptionSetLocales.NormalizeTranslations(request.Translations, _settings),
            audit,
            now);
        set.Status = request.Status;
        set.Version++;
        set.UpdatedAt = now;
        set.UpdatedBy = audit;
    }

    private Task RecordTranslationAsync(OptionSet set, OptionSetWriteRequestDto request,
        CancellationToken cancellationToken) => _translationProvenance.RecordAsync(
        "optionSet", set.Id,
        request.TranslationMetadata ?? new TranslationOwnerMetadataDto
        {
            SourceLocales = new Dictionary<string, string> { ["name"] = set.SourceLocale }
        },
        TranslationTextMap.Create(set.Name, null,
            set.Translations.Select(row => (row.LanguageCode, (string?)row.Name, (string?)null))),
        cancellationToken);

    private static OptionSetEntry? ResolveExistingEntry(
        OptionSetEntryDto dto,
        ICollection<OptionSetEntry> entries,
        Dictionary<Guid, OptionSetEntry> byId,
        HashSet<Guid> retained)
    {
        if (dto.Id is Guid id)
        {
            if (!byId.TryGetValue(id, out var byRequestedId) || !retained.Add(id))
            {
                throw new BadRequestException("An option-set entry ID is invalid or repeated");
            }

            return byRequestedId;
        }

        var match = entries.FirstOrDefault(entry => !retained.Contains(entry.Id) && SameReference(entry, dto));
        if (match is not null)
        {
            retained.Add(match.Id);
        }

        return match;
    }

    private static OptionSetEntry NewEntry(OptionSetEntryDto dto, string audit, DateTime now)
    {
        var entry = new OptionSetEntry { Id = Guid.NewGuid(), CreatedAt = now, CreatedBy = audit };
        CopyEntry(dto, entry, audit, now);
        entry.UpdatedAt = null;
        entry.UpdatedBy = null;
        return entry;
    }

    private static void CopyEntry(OptionSetEntryDto dto, OptionSetEntry entry, string audit, DateTime now)
    {
        entry.Name = dto.Name.Trim();
        entry.DisplayOrder = dto.DisplayOrder;
        entry.GlobalIngredientId = dto.GlobalIngredientId;
        entry.ProductId = dto.ProductId;
        entry.ProductVariationId = dto.ProductVariationId;
        entry.IsOptional = dto.IsOptional;
        entry.MaxQuantity = dto.MaxQuantity;
        entry.Price = dto.Price;
        entry.IsIncludedInBasePrice = dto.IsIncludedInBasePrice;
        entry.IsRequired = dto.IsRequired;
        entry.AdditionalPrice = dto.AdditionalPrice;
        entry.IsDefault = dto.IsDefault;
        entry.UpdatedAt = entry.Id == Guid.Empty ? null : now;
        entry.UpdatedBy = entry.Id == Guid.Empty ? null : audit;
    }

    private static string CanonicalReference(OptionSetEntryDto entry) =>
        entry.GlobalIngredientId is Guid ingredientId
            ? $"ingredient:{ingredientId:D}"
            : $"product:{entry.ProductId:D}:{entry.ProductVariationId:D}";

    private static bool SameReference(OptionSetEntry entry, OptionSetEntryDto dto) =>
        entry.GlobalIngredientId == dto.GlobalIngredientId
        && entry.ProductId == dto.ProductId
        && entry.ProductVariationId == dto.ProductVariationId;

    private sealed record UpdatePlan(
        List<(OptionSetEntryDto Dto, OptionSetEntry? Existing)> Entries,
        List<OptionSetEntryDto> EntriesToValidate,
        HashSet<Guid> RetainedIds);
}
