using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService
{
    public async Task<CreateOrReuseImportedSetResult> CreateOrReuseImportedSetAsync(
        CreateOrReuseImportedSetRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourceTemplateId)
            || string.IsNullOrWhiteSpace(request.SourceOptionSetId)
            || request.SourceTemplateId.Length > _settings.MaximumSourceIdentifierLength
            || request.SourceOptionSetId.Length > _settings.MaximumSourceIdentifierLength
            || request.SourceRevision <= 0
            || string.IsNullOrWhiteSpace(request.Name)
            || !Enum.IsDefined(request.Kind))
        {
            throw new BadRequestException("The central option-set revision reference is incomplete");
        }

        var sourceLocale = OptionSetLocales.NormalizeLocale(request.SourceLocale, _settings.MaximumLocaleTagLength);
        var translations = OptionSetLocales.NormalizeTranslations(request.Translations, _settings);
        var existing = await _context.OptionSets.AsNoTracking().AsSplitQuery().Include(set => set.Entries)
            .Include(set => set.Translations)
            .FirstOrDefaultAsync(set => set.SourceTemplateId == request.SourceTemplateId
                && set.SourceRevision == request.SourceRevision
                && set.SourceOptionSetId == request.SourceOptionSetId, cancellationToken);
        if (existing is not null)
        {
            return ImportedResult(existing, created: false, request.Entries);
        }

        var write = new OptionSetWriteRequestDto
        {
            Kind = request.Kind,
            Name = request.Name,
            SourceLocale = sourceLocale,
            Translations = translations,
            Entries = request.Entries.Select(ImportedEntry).ToList()
        };
        ValidateHeader(write, creating: true);
        ValidateEntryCount(write);
        await ValidateEntriesAsync(
            write.Kind, write.Entries, cancellationToken, request.StagedProductIds);
        EnsureDistinctSourceEntries(request.Entries, _settings);
        var name = await ResolveImportedNameAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        var actor = _currentUser.GetAuditIdentifier();
        var set = new OptionSet
        {
            Kind = request.Kind,
            Name = name,
            SourceLocale = sourceLocale,
            NormalizedName = OptionSetNameNormalizer.Normalize(name),
            SourceTemplateId = request.SourceTemplateId,
            SourceRevision = request.SourceRevision,
            SourceOptionSetId = request.SourceOptionSetId,
            CreatedAt = now,
            CreatedBy = actor,
            Entries = request.Entries.Select(entry => ImportedEntity(entry, actor, now)).ToList()
        };
        set.Translations = OptionSetLocales.CreateEntities(set, translations, actor, now);
        await _context.OptionSets.AddAsync(set, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return ImportedResult(set, created: true, request.Entries);
    }

    private async Task<string> ResolveImportedNameAsync(CreateOrReuseImportedSetRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var normalized = OptionSetNameNormalizer.Normalize(name);
        if (!await _context.OptionSets.AnyAsync(set => set.Kind == request.Kind && set.NormalizedName == normalized, cancellationToken))
        {
            return name;
        }

        var sourceId = request.SourceOptionSetId;
        var fingerprintSource = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId))).ToLowerInvariant();
        var fingerprint = fingerprintSource[..Math.Min(
            fingerprintSource.Length, _settings.MaximumImportedSourceFingerprintLength)];
        var sourceLabel = sourceId[..Math.Min(sourceId.Length, _settings.MaximumImportedSourceLabelLength)];
        var suffix = $"{OptionSetAuthoringSettings.ImportedSourceLabelSeparator}{sourceLabel}"
            + $"{OptionSetAuthoringSettings.ImportedSourceFingerprintSeparator}{fingerprint}";
        if (suffix.Length > _settings.MaximumOptionSetNameLength)
        {
            throw new BadRequestException("Configured imported-name suffix exceeds the option-set name limit");
        }

        var prefixLength = Math.Max(0, _settings.MaximumOptionSetNameLength - suffix.Length);
        var prefix = name[..Math.Min(name.Length, prefixLength)].TrimEnd();
        var available = $"{prefix}{suffix}";
        if (await _context.OptionSets.AnyAsync(set => set.Kind == request.Kind
            && set.NormalizedName == OptionSetNameNormalizer.Normalize(available), cancellationToken))
        {
            throw new ConflictException("A matching imported option set name already exists for a different source revision");
        }

        return available;
    }

    private static void EnsureDistinctSourceEntries(
        IReadOnlyList<ImportedOptionSetEntryRequest> entries,
        OptionSetAuthoringSettings settings)
    {
        if (entries.Count > settings.MaximumEntriesPerOptionSet
            || entries.Any(entry => string.IsNullOrWhiteSpace(entry.SourceEntryId)
                || entry.SourceEntryId.Length > settings.MaximumSourceIdentifierLength)
            || entries.Select(entry => entry.SourceEntryId).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new BadRequestException(
                $"Imported option entries need unique source IDs up to {settings.MaximumSourceIdentifierLength} characters and a maximum of {settings.MaximumEntriesPerOptionSet} rows");
        }
    }

    private static OptionSetEntryDto ImportedEntry(ImportedOptionSetEntryRequest entry) => new()
    {
        Name = entry.Name ?? string.Empty,
        DisplayOrder = entry.DisplayOrder,
        GlobalIngredientId = entry.GlobalIngredientId,
        ProductId = entry.ProductId,
        ProductVariationId = entry.ProductVariationId,
        IsOptional = entry.IsOptional,
        MaxQuantity = entry.MaxQuantity,
        Price = entry.Price,
        IsIncludedInBasePrice = entry.IsIncludedInBasePrice,
        IsRequired = entry.IsRequired,
        AdditionalPrice = entry.AdditionalPrice,
        IsDefault = entry.IsDefault
    };

    private static OptionSetEntry ImportedEntity(ImportedOptionSetEntryRequest dto, string actor, DateTime now)
    {
        var entry = NewEntry(ImportedEntry(dto), actor, now);
        entry.SourceEntryId = dto.SourceEntryId;
        return entry;
    }

    private static CreateOrReuseImportedSetResult ImportedResult(
        OptionSet set,
        bool created,
        IReadOnlyList<ImportedOptionSetEntryRequest> requested) => new()
        {
            OptionSetId = set.Id,
            Version = set.Version,
            Created = created,
            SourceLocale = set.SourceLocale,
            Translations = OptionSetLocales.ToDto(set.Translations),
            Entries = requested.Select(source => new ImportedOptionSetEntryResult
            {
                SourceEntryId = source.SourceEntryId,
                OptionSetEntryId = set.Entries.Single(entry => entry.SourceEntryId == source.SourceEntryId).Id
            }).ToList()
        };
}
