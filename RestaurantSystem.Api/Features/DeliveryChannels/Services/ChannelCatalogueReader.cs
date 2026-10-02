using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ChannelCatalogueReader(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options)
    : IChannelCatalogueReader
{
    public async Task<ChannelCatalogueSnapshot> Read(ChannelCatalogueRequest request, CancellationToken cancellationToken)
    {
        ChannelCatalogueSelection.Require(request.Items);
        ChannelDecisionBinding.Require(request.Provider, request.StoreId, request.Currency, request.IsSandbox, options);
        if (request.Language is not ("en" or "nl" or "fr" or "de" or "tr" or "ar"))
            throw new BadRequestException("Select a supported catalogue language.");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var currency = await context.RestaurantInfo.AsNoTracking().Select(row => row.Currency).SingleAsync(cancellationToken);
        if (currency != request.Currency) throw new BadRequestException("Marketplace and tenant currency must match.");
        var ids = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await context.Products.AsNoTracking().Where(row => ids.Contains(row.Id))
            .Include(row => row.Descriptions).Include(row => row.Variations).ThenInclude(row => row.Descriptions)
            .Include(row => row.CustomizationGroups).Include(row => row.DetailedIngredients)
            .Include(row => row.ProductCategories).ThenInclude(row => row.Category)
            .AsSplitQuery().ToDictionaryAsync(row => row.Id, cancellationToken);
        var items = request.Items.OrderBy(item => item.ProductId).ThenBy(item => item.VariationId)
            .Select(item => Map(item, products.GetValueOrDefault(item.ProductId), request.Language)).ToArray();
        await transaction.CommitAsync(cancellationToken);
        var revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { request.Provider, request.StoreId, request.Currency, request.IsSandbox, request.Language, Items = items })));
        return new(request.Provider, request.StoreId, request.Currency, request.IsSandbox, request.Language, revision, items);
    }

    private static ChannelCatalogueItem Map(ChannelAvailabilitySelection selection, Product? product, string language)
    {
        var reason = Eligibility(product);
        if (reason.Length > 0 || product is null) return Block(selection, reason);
        var translations = product.Descriptions.Where(row => row.Lang == language).ToArray();
        if (translations.Length != 1) return Block(selection, "MissingTranslation");
        var translation = translations[0];
        if (string.IsNullOrWhiteSpace(translation.Name)) return Block(selection, "InvalidText");
        var variation = selection.VariationId.HasValue ? product.Variations.SingleOrDefault(row => row.Id == selection.VariationId && row.IsActive) : null;
        if (selection.VariationId.HasValue && variation is null) return Block(selection, "UnavailableVariation");
        if (!selection.VariationId.HasValue && BaseProductVisibility.IsBaseHidden(product)) return Block(selection, "VariationRequired");
        var text = Text(translation, variation, language);
        if (text.BlockReason.Length > 0) return Block(selection, text.BlockReason);
        var minor = (product.BasePrice + (variation?.PriceModifier ?? 0)) * ExternalOrderLimits.MinorUnitsPerWholeUnit;
        if (minor < 0 || minor > int.MaxValue || decimal.Truncate(minor) != minor) return Block(selection, "InvalidPrice");
        return new(selection.ProductId, selection.VariationId, text.Name, text.Description, text.VariationName, (int)minor,
            product.IsAvailable, string.Empty);
    }

    private static (string Name, string Description, string? VariationName, string BlockReason) Text(
        ProductDescription translation, ProductVariation? variation, string language)
    {
        var names = variation?.Descriptions.Where(row => row.LanguageCode == language).ToArray() ?? [];
        if (variation is not null && (names.Length != 1 || string.IsNullOrWhiteSpace(names[0].Name))) return ("", "", null, "MissingTranslation");
        var variationName = names.SingleOrDefault()?.Name;
        if (variationName is not null && (variationName.Length > ExternalOrderLimits.VariationNameLength || variationName.Any(char.IsControl))) return ("", "", null, "InvalidText");
        var name = variationName is null ? translation.Name : $"{translation.Name} — {variationName}";
        var variationDescription = names.SingleOrDefault()?.Description;
        var description = Description(translation.Description, variationDescription);
        if (name.Length > ExternalOrderLimits.ItemNameLength || description.Length > ExternalOrderLimits.CatalogueDescriptionLength
            || name.Any(char.IsControl)) return ("", "", null, "InvalidText");
        return (name, description, variationName, string.Empty);
    }

    private static ChannelCatalogueItem Block(ChannelAvailabilitySelection selection, string reason)
        => new(selection.ProductId, selection.VariationId, string.Empty, string.Empty, null, null, false, reason);

    private static string Eligibility(Product? product) => product switch
    {
        null => "MissingProduct",
        { IsActive: false } or { IsComponent: true } => "UnavailableProduct",
        _ when !OrderChannelMap.Allows(OrderTypeAvailability.EffectiveMask(product), OrderType.Delivery) => "DeliveryDisabled",
        _ when ChannelProductContract.IsUnsupported(product) => "UnsupportedChoices",
        { Allergens.Count: > 0 } => "UnmappedAllergens",
        _ => string.Empty,
    };

    private static string Description(string product, string? variation)
    {
        if (string.IsNullOrEmpty(variation)) return product;
        return string.IsNullOrEmpty(product) ? variation : $"{product}\n{variation}";
    }
}
