using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ExternalOrderImporter(ApplicationDbContext context, IOptions<DeliveryChannelSettings> options,
    ICurrentUserService currentUser, IOrderNumberGenerator orderNumbers) : IExternalOrderImporter
{
    public async Task<ExternalOrderImportDto> ImportAsync(ExternalOrderRequest request, CancellationToken cancellationToken)
    {
        var binding = await ValidateBinding(request, cancellationToken);

        // Includes canonical evidence AND normalized item identities/prices/instructions. A changed
        // transformation cannot masquerade as the same import by reusing the provider body hash.
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({request.Provider + ":" + request.StoreId + ":" + request.ExternalOrderId}, 0))", cancellationToken);
        var existing = await context.ExternalOrderReferences.FirstOrDefaultAsync(reference =>
            reference.Provider == request.Provider && reference.ExternalStoreId == request.StoreId
            && reference.ExternalOrderId == request.ExternalOrderId, cancellationToken);
        if (existing is not null)
            return await Replay(existing, fingerprint, cancellationToken);

        await ValidateProducts(request, cancellationToken);

        var audit = currentUser.GetAuditIdentifier();
        var order = ExternalOrderBuilder.Build(request, binding.IsSandbox, fingerprint, audit);
        order.OrderNumber = await orderNumbers.GenerateAsync(cancellationToken);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ExternalOrderImportDto(order.Id, order.OrderNumber, false);
    }

    private async Task<DeliveryChannelStore> ValidateBinding(ExternalOrderRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new ForbiddenException("Delivery-channel ingestion is disabled for this tenant.");
        var bindings = settings.Stores.Where(store => store.Provider == request.Provider && store.StoreId == request.StoreId).ToArray();
        if (bindings.Length != 1 || settings.SandboxOnly && !bindings[0].IsSandbox)
            throw new ForbiddenException("This marketplace store is not bound to this tenant.");
        var binding = bindings[0];
        var tenantCurrency = await context.RestaurantInfo.AsNoTracking().Select(info => info.Currency).FirstOrDefaultAsync(cancellationToken);
        if (request.Currency != binding.Currency || !string.Equals(request.Currency, tenantCurrency, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Marketplace and tenant currencies must match.");
        if (request.Items.Sum(item => item.Total) != request.MerchantTotal)
            throw new BadRequestException("Marketplace totals contain an unsupported adjustment or do not match the items.");

        return binding;
    }

    private async Task<ExternalOrderImportDto> Replay(ExternalOrderReference existing, string fingerprint,
        CancellationToken cancellationToken)
    {
        if (existing.PayloadHash != fingerprint)
            throw new ConflictException("This marketplace order was already imported with different content.");
        var persisted = await context.Orders.FirstOrDefaultAsync(order => order.Id == existing.OrderId, cancellationToken);
        if (persisted is null)
            throw new ConflictException("This marketplace order was already imported and later removed. Its identity cannot be reused.");
        return new ExternalOrderImportDto(persisted.Id, persisted.OrderNumber, true);
    }

    private async Task ValidateProducts(ExternalOrderRequest request, CancellationToken cancellationToken)
    {
        var productIds = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await context.Products.Include(product => product.Variations)
            .Include(product => product.CustomizationGroups)
            .Include(product => product.DetailedIngredients)
            .Include(product => product.ProductCategories).ThenInclude(category => category.Category)
            .AsSplitQuery()
            .Where(product => productIds.Contains(product.Id)).ToDictionaryAsync(product => product.Id, cancellationToken);
        foreach (var item in request.Items)
            ValidateProduct(item, products.GetValueOrDefault(item.ProductId));

    }

    private static void ValidateProduct(ExternalOrderItemRequest item, Product? product)
    {
        if (product is null || !product.IsActive || !product.IsAvailable
            || product.IsComponent || !OrderChannelMap.Allows(OrderTypeAvailability.EffectiveMask(product), OrderType.Delivery))
            throw new BadRequestException("A marketplace item has no active, available tenant product mapping.");
        if (item.VariationId.HasValue && !product.Variations.Any(variation => variation.Id == item.VariationId && variation.IsActive))
            throw new BadRequestException("A marketplace variation does not belong to its mapped tenant product.");
        if (!item.VariationId.HasValue && BaseProductVisibility.IsBaseHidden(product))
            throw new BadRequestException("This marketplace product requires a mapped variation.");
        if (product.Type == ProductType.Menu || product.CustomizationGroups.Any(group => group.IsActive)
            || product.SauceMin > 0 || product.DetailedIngredients.Count > 0)
            throw new BadRequestException("Bundles and modifier products require the full channel mapping contract before import.");
    }
}
