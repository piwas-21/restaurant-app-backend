using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
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
        var translations = OptionSetLocales.NormalizeTranslations(request.Translations);
        var set = new OptionSet
        {
            Kind = request.Kind,
            Name = name,
            SourceLocale = OptionSetLocales.NormalizeLocale(request.SourceLocale),
            NormalizedName = normalizedName,
            Status = OptionSetStatus.Active,
            Version = 1,
            CreatedAt = now,
            CreatedBy = audit,
            Entries = request.Entries.Select(entry => NewEntry(entry, audit, now)).ToList()
        };
        set.Translations = OptionSetLocales.CreateEntities(set, translations, audit, now);
        await _context.OptionSets.AddAsync(set, cancellationToken);
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

        var set = await _context.OptionSets.Include(item => item.Entries).Include(item => item.Attachments)
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
        var byId = set.Entries.ToDictionary(entry => entry.Id);
        var retained = new HashSet<Guid>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var plannedEntries = new List<(OptionSetEntryDto Dto, OptionSetEntry? Existing)>();
        var entriesToValidate = new List<OptionSetEntryDto>();
        var now = DateTime.UtcNow;
        var audit = _currentUser.GetAuditIdentifier();

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

        await ValidateEntriesAsync(request.Kind, entriesToValidate, cancellationToken);
        var entryIds = set.Entries.Select(entry => entry.Id).ToArray();
        HashSet<Guid> materializedEntryIds = entryIds.Length == 0
            ? []
            : (await _context.OptionSetAppliedRows.Where(row => entryIds.Contains(row.OptionSetEntryId))
                .Select(row => row.OptionSetEntryId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        foreach (var (dto, existingEntry) in plannedEntries)
        {
            var entry = existingEntry;
            if (entry is null)
            {
                entry = NewEntry(dto, audit, now);
                entry.OptionSetId = set.Id;
                entry.OptionSet = set;
                set.Entries.Add(entry);
                _context.OptionSetEntries.Add(entry);
                retained.Add(entry.Id);
            }
            else
            {
                if (materializedEntryIds.Contains(entry.Id) && !SameReference(entry, dto))
                {
                    throw new ConflictException("Detach this entry from its targets before changing its canonical item");
                }

                CopyEntry(dto, entry, audit, now);
                entry.IsEnabled = true;
                retained.Add(entry.Id);
            }
        }

        foreach (var entry in set.Entries.Where(entry => entry.IsEnabled && !retained.Contains(entry.Id)))
        {
            entry.IsEnabled = false;
            entry.UpdatedAt = now;
            entry.UpdatedBy = audit;
        }

        set.Name = request.Name.Trim();
        set.SourceLocale = OptionSetLocales.NormalizeLocale(request.SourceLocale);
        set.NormalizedName = OptionSetNameNormalizer.Normalize(set.Name);
        OptionSetLocales.Sync(
            set.Translations,
            OptionSetLocales.NormalizeTranslations(request.Translations),
            audit,
            now);
        set.Status = request.Status;
        set.Version++;
        set.UpdatedAt = now;
        set.UpdatedBy = audit;
        await SaveWithConflictTranslationAsync(cancellationToken);
        return ToDetail(set);
    }

    private async Task ValidateEntriesAsync(
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntryDto> entries,
        CancellationToken cancellationToken,
        IReadOnlySet<Guid>? stagedProductIds = null)
    {
        var errors = await OptionSetEntryValidator.ValidateManyAsync(
            _context, kind, entries, stagedProductIds: stagedProductIds, cancellationToken: cancellationToken);
        foreach (var error in errors)
        {
            if (error is not null)
            {
                throw new BadRequestException(error);
            }
        }
    }

    private static void ValidateEntryCount(OptionSetWriteRequestDto request)
    {
        if (request.Entries.Count > 200)
        {
            throw new BadRequestException("An option set may contain at most 200 entries");
        }
    }

    private static void ValidateHeader(OptionSetWriteRequestDto request, bool creating)
    {
        if (!Enum.IsDefined(request.Kind) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            throw new BadRequestException("Choose a valid option-set kind and a name up to 120 characters");
        }

        if (!Enum.IsDefined(request.Status) || (creating && request.Status != OptionSetStatus.Active))
        {
            throw new BadRequestException("New option sets must start active");
        }
    }

    private async Task SaveWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("This option set changed. Reload it and review the current values.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException("An option set with the same name or canonical entry already exists", exception);
        }
    }

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
}
