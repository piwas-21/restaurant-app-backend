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

public sealed class ChannelAvailabilityReader(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options)
    : IChannelAvailabilityReader
{
    public async Task<ChannelAvailabilitySnapshot> Read(ChannelAvailabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count is < 1 or > ExternalOrderLimits.MaxItems
            || request.Items.Any(item => item is null || item.ProductId == Guid.Empty || item.VariationId == Guid.Empty)
            || request.Items.Distinct().Count() != request.Items.Count)
            throw new BadRequestException("Select 1–200 distinct product/variation identities for availability.");
        ChannelDecisionBinding.Require(request.Provider, request.StoreId, request.Currency, request.IsSandbox, options);
        // Split includes must describe one catalogue snapshot even while staff edit stock or choices.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var currency = await context.RestaurantInfo.AsNoTracking().Select(tenant => tenant.Currency).SingleAsync(cancellationToken);
        if (currency != request.Currency) throw new BadRequestException("Marketplace and tenant currency must match.");
        var ids = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await context.Products.AsNoTracking().Where(product => ids.Contains(product.Id))
            .Include(product => product.Variations).Include(product => product.CustomizationGroups)
            .Include(product => product.DetailedIngredients)
            .Include(product => product.ProductCategories).ThenInclude(assignment => assignment.Category)
            .AsSplitQuery().ToDictionaryAsync(product => product.Id, cancellationToken);
        var items = request.Items.OrderBy(item => item.ProductId).ThenBy(item => item.VariationId)
            .Select(item => Map(item, products.GetValueOrDefault(item.ProductId))).ToArray();
        await transaction.CommitAsync(cancellationToken);
        var revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { request.Provider, request.StoreId, request.Currency, request.IsSandbox, Items = items })));
        return new(request.Provider, request.StoreId, request.Currency, request.IsSandbox, revision, items);
    }

    private static ChannelAvailabilityItem Map(ChannelAvailabilitySelection selection, Product? product)
    {
        var reason = product switch
        {
            null => "MissingProduct",
            { IsActive: false } or { IsAvailable: false } or { IsComponent: true } => "UnavailableProduct",
            _ when !OrderChannelMap.Allows(OrderTypeAvailability.EffectiveMask(product), OrderType.Delivery) => "DeliveryDisabled",
            _ when ChannelProductContract.IsUnsupported(product) => "UnsupportedChoices",
            _ when selection.VariationId.HasValue && !product.Variations.Any(variation => variation.Id == selection.VariationId && variation.IsActive)
                => "UnavailableVariation",
            _ when !selection.VariationId.HasValue && BaseProductVisibility.IsBaseHidden(product) => "VariationRequired",
            _ => "Available",
        };
        return new(selection.ProductId, selection.VariationId, reason == "Available", reason);
    }
}
