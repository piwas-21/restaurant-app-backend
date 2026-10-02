using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCandidatesQuery;

public sealed record GetChannelCatalogueCandidatesQuery(string Search = "", string Cursor = "", string Language = "en")
    : IQuery<ChannelCatalogueCandidatesDto>;

public sealed class GetChannelCatalogueCandidatesQueryHandler(ApplicationDbContext context,
    IChannelCatalogueReader catalogue, IOptions<DeliveryChannelSettings> options)
    : IQueryHandler<GetChannelCatalogueCandidatesQuery, ChannelCatalogueCandidatesDto>
{
    private const int PageSize = 50;
    private const int MaximumOffset = 100_000;
    private const int MaximumSearchLength = 100;

    public async Task<ChannelCatalogueCandidatesDto> Handle(GetChannelCatalogueCandidatesQuery query, CancellationToken cancellationToken)
    {
        var offset = Offset(query.Cursor);
        var search = query.Search.Trim();
        if (search.Length > MaximumSearchLength || search.Any(char.IsControl)
            || query.Language is not ("en" or "nl" or "fr" or "de" or "tr" or "ar"))
            throw new BadRequestException("Select a supported language and a search of up to 100 characters.");
        var bindings = options.Value.Stores.Where(row => row.Provider == "uber-eats" && row.IsSandbox).ToArray();
        if (!options.Value.Enabled || bindings.Length != 1)
            throw new ForbiddenException("This marketplace store is not enabled for this tenant.");
        var binding = bindings[0];
        var currency = await context.RestaurantInfo.AsNoTracking().Select(row => row.Currency).SingleAsync(cancellationToken);
        if (currency != binding.Currency) throw new BadRequestException("Marketplace and tenant currency must match.");

        // Page the flattened identities in SQL, so a product with many variations cannot expand an unbounded page.
        var products = context.Products.AsNoTracking().Where(row => !row.IsComponent);
        var candidates = products.Select(row => new
        {
            ProductId = row.Id,
            VariationId = (Guid?)null,
            Name = row.Descriptions.Where(text => text.Lang == query.Language).Select(text => text.Name).FirstOrDefault() ?? row.Name,
            VariationName = (string?)null
        })
            .Concat(products.SelectMany(row => row.Variations.Where(variation => variation.IsActive), (row, variation) =>
                new
                {
                    ProductId = row.Id,
                    VariationId = (Guid?)variation.Id,
                    Name = row.Descriptions.Where(text => text.Lang == query.Language).Select(text => text.Name).FirstOrDefault() ?? row.Name,
                    VariationName = (string?)(variation.Descriptions.Where(text => text.LanguageCode == query.Language).Select(text => text.Name).FirstOrDefault() ?? variation.Name)
                }));
        if (search.Length > 0)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            candidates = candidates.Where(row => EF.Functions.ILike(row.Name, pattern, "\\")
                || row.VariationName != null && EF.Functions.ILike(row.VariationName, pattern, "\\"));
        }
        var page = await candidates.OrderBy(row => row.ProductId).ThenBy(row => row.VariationId)
            .Skip(offset).Take(PageSize + 1).ToArrayAsync(cancellationToken);
        var selected = page.Take(PageSize).ToArray();
        if (selected.Length == 0) return new(currency, query.Language, null, []);
        var snapshot = await catalogue.Read(new()
        {
            Provider = binding.Provider,
            StoreId = binding.StoreId,
            Currency = currency,
            IsSandbox = true,
            Language = query.Language,
            Items = selected.Select(row => new ChannelAvailabilitySelection(row.ProductId, row.VariationId)).ToList()
        }, cancellationToken);
        var byIdentity = snapshot.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        var items = selected.Select(row =>
        {
            var item = byIdentity[(row.ProductId, row.VariationId)];
            return new ChannelCatalogueCandidateDto(row.ProductId, row.VariationId,
                item.Name.Length > 0 ? item.Name : row.Name, item.VariationName ?? row.VariationName,
                item.PriceMinor, item.Available, item.BlockReason.Length == 0, item.BlockReason);
        }).ToArray();
        return new(currency, query.Language, page.Length > PageSize ? Cursor(offset + PageSize) : null, items);
    }

    private static int Offset(string cursor)
    {
        if (cursor.Length == 0) return 0;
        try
        {
            if (cursor.Length > 32) throw InvalidCursor();
            var decoded = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0 || offset > MaximumOffset || offset % PageSize != 0 || Cursor(offset) != cursor)
                throw InvalidCursor();
            return offset;
        }
        catch (FormatException) { throw InvalidCursor(); }
    }

    private static string Cursor(int offset)
        => WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static BadRequestException InvalidCursor() => new("Refresh the product search to start a valid page.");

}
