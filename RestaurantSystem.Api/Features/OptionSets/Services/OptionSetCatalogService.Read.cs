using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public sealed partial class OptionSetCatalogService
{
    public async Task<OptionSetPageDto> SearchAsync(
        OptionSetKind? kind,
        string? query,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var pageSize = PageSize(limit);
        var sets = _context.OptionSets.AsNoTracking().AsQueryable();
        if (kind.HasValue)
        {
            sets = sets.Where(set => set.Kind == kind.Value);
        }

        var normalized = string.IsNullOrWhiteSpace(query) ? null : OptionSetNameNormalizer.Normalize(query);
        if (normalized is not null)
        {
            var pattern = $"%{normalized}%";
            sets = sets.Where(set => EF.Functions.Like(set.NormalizedName, pattern)
                || set.Translations.Any(translation => EF.Functions.ILike(translation.Name, pattern, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var lastId = OptionSetPageCursor.Decode(cursor);
            sets = sets.Where(set => set.Id.CompareTo(lastId) > 0);
        }

        var rows = await sets.OrderBy(set => set.Id)
            .Select(set => new OptionSetSummaryDto
            {
                Id = set.Id,
                Kind = set.Kind,
                Name = set.Name,
                SourceLocale = set.SourceLocale,
                Status = set.Status,
                Version = set.Version,
                EntryCount = set.Entries.Count(entry => entry.IsEnabled),
                AttachmentCount = set.Attachments.Count,
                UpdatedAt = set.UpdatedAt,
                SourceTemplateId = set.SourceTemplateId,
                SourceRevision = set.SourceRevision
            })
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var last = rows.LastOrDefault();
        return new OptionSetPageDto
        {
            Items = rows,
            NextCursor = hasMore && last is not null
                ? OptionSetPageCursor.Encode(last.Id)
                : null
        };
    }

    public async Task<OptionSetDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var set = await _context.OptionSets.AsNoTracking().AsSplitQuery()
            .Include(item => item.Entries)
            .Include(item => item.Attachments)
            .Include(item => item.Translations)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Option set", id);
        return ToDetail(set);
    }

    private static OptionSetDetailDto ToDetail(OptionSet set) => new()
    {
        Id = set.Id,
        Kind = set.Kind,
        Name = set.Name,
        SourceLocale = set.SourceLocale,
        Translations = OptionSetLocales.ToDto(set.Translations),
        Status = set.Status,
        Version = set.Version,
        Entries = set.Entries.Where(entry => entry.IsEnabled).OrderBy(entry => entry.DisplayOrder)
            .ThenBy(entry => entry.Id).Select(entry => new OptionSetEntryDto
            {
                Id = entry.Id,
                Name = entry.Name,
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
            }).ToList(),
        Attachments = set.Attachments.OrderBy(attachment => attachment.TargetProductId)
            .ThenBy(attachment => attachment.TargetMenuSectionId)
            .Select(attachment => new OptionSetAttachmentDto
            {
                Id = attachment.Id,
                Role = attachment.Role,
                TargetProductId = attachment.TargetProductId,
                TargetMenuSectionId = attachment.TargetMenuSectionId,
                TargetCustomizationGroupId = attachment.TargetCustomizationGroupId,
                AppliedSetVersion = attachment.AppliedSetVersion,
                Version = attachment.Version,
                MinSelection = attachment.MinSelection,
                MaxSelection = attachment.MaxSelection,
                IncludedFree = attachment.IncludedFree,
                DisplayOrder = attachment.DisplayOrder,
                IntentionalDifferenceReason = attachment.IntentionalDifferenceReason
            }).ToList(),
        SourceTemplateId = set.SourceTemplateId,
        SourceRevision = set.SourceRevision,
        TranslationMetadata = new TranslationOwnerMetadataDto
        {
            SourceLocales = new Dictionary<string, string> { ["name"] = set.SourceLocale }
        }
    };
}
