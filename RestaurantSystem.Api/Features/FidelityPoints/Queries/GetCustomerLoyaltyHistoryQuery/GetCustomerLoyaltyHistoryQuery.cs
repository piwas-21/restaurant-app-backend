using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyHistoryQuery;

public sealed record GetCustomerLoyaltyHistoryQuery(int? SchemaVersion, DateTime? BeforeCreatedAt = null,
    Guid? BeforeId = null) : IQuery<ApiResponse<CustomerLoyaltyHistoryDto>>;

public sealed class GetCustomerLoyaltyHistoryQueryHandler(ApplicationDbContext context,
    CustomMediator mediator, ICurrentUserService current)
    : IQueryHandler<GetCustomerLoyaltyHistoryQuery, ApiResponse<CustomerLoyaltyHistoryDto>>
{
    private const int PageSize = 20;
    public async Task<ApiResponse<CustomerLoyaltyHistoryDto>> Handle(GetCustomerLoyaltyHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var authority = await CustomerLoyaltyReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        var accounts = CustomerAccountAccess.ForOwner(context, authority.Owner);
        var rows = context.FidelityPointsTransactions.AsNoTracking()
            .Where(row => row.UserId == authority.Owner && accounts.Any(user => user.Id == row.UserId));
        if (query.BeforeCreatedAt is { } created && query.BeforeId is { } id)
            rows = rows.Where(row => row.CreatedAt < created || row.CreatedAt == created && row.Id.CompareTo(id) > 0);
        var found = await rows.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id).Take(PageSize + 1)
            .Select(row => new { row.Id, row.TransactionType, row.Points, row.CreatedAt, row.ExpiresAt })
            .ToArrayAsync(cancellationToken);
        await CustomerLoyaltyReadAccess.RequireCurrentAsync(context, mediator, current, authority, cancellationToken);
        var items = found.Take(PageSize).Select(row => new CustomerLoyaltyMovementDto(row.Id,
            Kind(row.TransactionType), row.Points, row.CreatedAt, row.ExpiresAt)).ToArray();
        var last = items.LastOrDefault();
        CustomerLoyaltyCursorDto? next = found.Length > PageSize && last is not null
            ? new(last.CreatedAt, last.TransactionId) : null;
        return ApiResponse<CustomerLoyaltyHistoryDto>.SuccessWithData(new(1, items, next));
    }

    private static string Kind(TransactionType type) => type switch
    {
        TransactionType.Earned => "earned",
        TransactionType.Redeemed => "redeemed",
        TransactionType.AdminAdjustment => "adjusted",
        TransactionType.Expired => "expired",
        TransactionType.Refunded => "refunded",
        TransactionType.EarnedClawback => "earned-reversed",
        TransactionType.RedemptionRestored => "redemption-restored",
        _ => throw new ConflictException("The loyalty history requires reconciliation.")
    };
}
