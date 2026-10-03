using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoriesQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCandidatesQuery;

public sealed record GetChannelCatalogueCandidatesQuery(string Search = "", string Cursor = "",
    string Language = "", Guid? CategoryId = null, string SourceRevision = "") : IQuery<ChannelCatalogueCandidatesDto>;

public sealed class GetChannelCatalogueCandidatesQueryHandler(IChannelCatalogueInventoryReader inventory,
    IOptions<DeliveryChannelSettings> options, IEmailLanguageResolver languages)
    : IQueryHandler<GetChannelCatalogueCandidatesQuery, ChannelCatalogueCandidatesDto>
{
    private const int PageSize = 50;
    private const int MaximumOffset = 100_000;
    private const int MaximumSearchLength = 100;

    public async Task<ChannelCatalogueCandidatesDto> Handle(GetChannelCatalogueCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        var language = query.Language.Length == 0 ? languages.TenantDefault : query.Language;
        var search = query.Search.Trim();
        if (search.Length > MaximumSearchLength || search.Any(char.IsControl)
            || language is not ("en" or "nl" or "fr" or "de" or "tr" or "ar"))
            throw new BadRequestException("Select a supported language and a search of up to 100 characters.");
        var binding = ChannelCatalogueBinding.Require(options.Value);
        var source = await inventory.Read(binding.Provider, binding.StoreId, binding.Currency, true, language, cancellationToken);
        var cursor = ReadCursor(query.Cursor, search, query.CategoryId, source.Revision, query.SourceRevision);
        var rows = source.Items.Where(row => (!query.CategoryId.HasValue || row.CategoryId == query.CategoryId)
                && (search.Length == 0 || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.VariationName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(row => row.CategoryDisplayOrder ?? int.MaxValue).ThenBy(row => row.CategoryId)
            .ThenBy(row => row.ItemDisplayOrder).ThenBy(row => row.ProductId).ThenBy(row => row.VariationId)
            .Skip(cursor.Offset).Take(PageSize + 1).ToArray();
        var items = rows.Take(PageSize).Select(row => new ChannelCatalogueCandidateDto(row.ProductId, row.VariationId,
            row.Name, row.VariationName, row.PriceMinor, row.Available, row.Supported, row.BlockReason,
            row.SelectionKey, row.CategoryId, row.CategoryName, row.CategoryDisplayOrder, row.ItemDisplayOrder)).ToArray();
        var next = rows.Length > PageSize ? Cursor(cursor.Offset + PageSize, search, query.CategoryId, source.Revision) : null;
        return new(source.Currency, source.Language, next, items, source.Revision);
    }

    private static CursorData ReadCursor(string cursor, string search, Guid? categoryId,
        string sourceRevision, string requestedRevision)
    {
        if (cursor.Length == 0)
        {
            if (requestedRevision.Length > 0 && requestedRevision != sourceRevision) throw Changed();
            return new(0);
        }
        try
        {
            if (cursor.Length > 512) throw InvalidCursor();
            var json = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var data = JsonSerializer.Deserialize<CursorData>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (data is null || data.Offset < 0 || data.Offset > MaximumOffset || data.Offset % PageSize != 0
                || data.Search != search || data.CategoryId != categoryId || !Revision(data.SourceRevision)
                || Cursor(data.Offset, search, categoryId, data.SourceRevision) != cursor)
                throw InvalidCursor();
            if (data.SourceRevision != sourceRevision || requestedRevision.Length > 0 && requestedRevision != sourceRevision)
                throw Changed();
            return data;
        }
        catch (FormatException) { throw InvalidCursor(); }
        catch (JsonException) { throw InvalidCursor(); }
    }

    private static string Cursor(int offset, string search, Guid? categoryId, string sourceRevision)
        => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new CursorData(offset, search, categoryId, sourceRevision),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static BadRequestException InvalidCursor() => new("Refresh the product search to start a valid page.");
    private static ConflictException Changed() => new("The tenant catalogue changed. Refresh categories and restart this search.", "SourceRevisionChanged");
    private static bool Revision(string value)
        => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
    private sealed record CursorData(int Offset, string Search = "", Guid? CategoryId = null, string SourceRevision = "");
}
