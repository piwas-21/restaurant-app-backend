using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using BasketReadQuery = RestaurantSystem.Api.Features.Basket.Queries.GetBasketQuery.GetBasketQuery;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;

namespace RestaurantSystem.Api.Features.Basket.Queries.GetCustomerBasketQuery;

public record GetCustomerBasketQuery(string SessionId) : IQuery<ApiResponse<BasketDto>>;

public sealed class GetCustomerBasketQueryHandler(
    CustomMediator mediator, ICurrentUserService currentUser, IHttpContextAccessor httpContext)
    : IQueryHandler<GetCustomerBasketQuery, ApiResponse<BasketDto>>
{
    public async Task<ApiResponse<BasketDto>> Handle(GetCustomerBasketQuery query, CancellationToken cancellationToken)
    {
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        CustomerBasketOperationHash.Session(query.SessionId);
        var bootstrap = await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken);
        if (bootstrap.Data?.Capabilities.GetValueOrDefault("ordering") != "enabled")
            throw new NotFoundException("Customer mobile ordering is unavailable.");
        return await mediator.SendQuery(new BasketReadQuery(query.SessionId), cancellationToken);
    }
}
