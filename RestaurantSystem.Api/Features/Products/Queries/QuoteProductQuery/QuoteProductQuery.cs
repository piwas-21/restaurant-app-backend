using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Queries.QuoteProductQuery;

public sealed record QuoteProductQuery(
    Guid ProductId,
    ProductQuoteRequestDto Request,
    OrderType? RequestedOrderType = null) : IQuery<ApiResponse<ProductQuoteDto>>;
