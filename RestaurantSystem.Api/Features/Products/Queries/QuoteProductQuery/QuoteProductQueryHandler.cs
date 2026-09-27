using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.QuoteProductQuery;

public sealed class QuoteProductQueryHandler(
    ApplicationDbContext context,
    IBasketItemFactory basketItemFactory)
    : IQueryHandler<QuoteProductQuery, ApiResponse<ProductQuoteDto>>
{
    public async Task<ApiResponse<ProductQuoteDto>> Handle(
        QuoteProductQuery query,
        CancellationToken cancellationToken)
    {
        var product = await BasketProductQuery.WithFactoryDependencies(context.Products)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == query.ProductId && candidate.IsActive && candidate.IsAvailable,
                cancellationToken);
        if (product is null)
        {
            throw new NotFoundException("Product not found or unavailable");
        }

        BasketChannelGuard.EnsureOrderable(product, query.RequestedOrderType);
        BasketComponentGuard.EnsureNotOrderedAlone(product);

        var request = ToBasketRequest(query.ProductId, query.Request);
        var basketItem = product.Type == ProductType.Menu
            ? await basketItemFactory.BuildMenuItemAsync(product, request, Guid.Empty, query.RequestedOrderType)
            : await BuildRegularItemAsync(product, request, query.RequestedOrderType, cancellationToken);

        var unitPrice = basketItem.ItemTotal / basketItem.Quantity;
        return ApiResponse<ProductQuoteDto>.SuccessWithData(new ProductQuoteDto
        {
            ProductId = product.Id,
            Quantity = basketItem.Quantity,
            UnitPrice = unitPrice,
            TotalPrice = basketItem.ItemTotal
        });
    }

    private async Task<BasketItem> BuildRegularItemAsync(
        Product product,
        AddToBasketDto request,
        OrderType? requestedOrderType,
        CancellationToken cancellationToken)
    {
        ProductVariation? variation = null;
        if (request.ProductVariationId is Guid variationId)
        {
            variation = product.Variations.FirstOrDefault(candidate =>
                candidate.Id == variationId && candidate.IsActive && !candidate.IsDeleted);
            if (variation is null)
            {
                throw new NotFoundException("Product variation not found or unavailable");
            }
        }

        BasketBaseProductGuard.EnsureVariationChosen(product, variation);
        if (product.CustomizationGroups.Any(group => group.IsActive))
        {
            var explicitSelection = ExplicitCustomizationSelection.Resolve(product, request.CustomizationSelections);
            request.SelectedIngredients = explicitSelection.SelectedIngredientIds;
            request.IngredientQuantities = explicitSelection.IngredientQuantities;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await basketItemFactory.BuildRegularItemAsync(
            product, variation, request, Guid.Empty, requestedOrderType);
    }

    private static AddToBasketDto ToBasketRequest(
        Guid productId,
        RestaurantSystem.Api.Features.Products.Dtos.Requests.ProductQuoteRequestDto request) => new()
        {
            ProductId = productId,
            ProductVariationId = request.ProductVariationId,
            Quantity = request.Quantity,
            SpecialInstructions = request.SpecialInstructions,
            SelectedIngredients = request.SelectedIngredients,
            AddedIngredients = request.AddedIngredients,
            IngredientQuantities = request.IngredientQuantities,
            CustomizationSelections = request.CustomizationSelections,
            SelectedSideItems = request.SelectedSideItems,
            SelectedMenuOptions = request.SelectedMenuOptions
        };
}
