using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyBalanceQuery;

public sealed record GetCustomerLoyaltyBalanceQuery(int? SchemaVersion) : IQuery<ApiResponse<CustomerLoyaltyBalanceDto>>;

public sealed class GetCustomerLoyaltyBalanceQueryHandler(ApplicationDbContext context,
    CustomMediator mediator, ICurrentUserService current)
    : IQueryHandler<GetCustomerLoyaltyBalanceQuery, ApiResponse<CustomerLoyaltyBalanceDto>>
{
    public async Task<ApiResponse<CustomerLoyaltyBalanceDto>> Handle(GetCustomerLoyaltyBalanceQuery query,
        CancellationToken cancellationToken)
    {
        var authority = await CustomerLoyaltyReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        var accounts = CustomerAccountAccess.ForOwner(context, authority.Owner);
        var balance = await context.FidelityPointBalances.AsNoTracking()
            .Where(row => row.UserId == authority.Owner && accounts.Any(user => user.Id == row.UserId))
            .Select(row => new CustomerLoyaltyBalanceDto(1, row.CurrentPoints, row.TotalEarnedPoints,
                row.TotalRedeemedPoints, row.LastUpdated)).SingleOrDefaultAsync(cancellationToken);
        await CustomerLoyaltyReadAccess.RequireCurrentAsync(context, mediator, current, authority, cancellationToken);
        if (balance is { CurrentPoints: < 0 } or { TotalEarnedPoints: < 0 } or { TotalRedeemedPoints: < 0 })
            throw new ConflictException("The loyalty balance requires reconciliation.");
        return ApiResponse<CustomerLoyaltyBalanceDto>.SuccessWithData(balance ?? new(1, 0, 0, 0, null));
    }
}
