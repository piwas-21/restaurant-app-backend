using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderHistoryQuery;

public sealed record GetCustomerOrderHistoryQuery(int? SchemaVersion, DateTime? BeforeOrderDate = null,
    Guid? BeforeOrderId = null) : IQuery<ApiResponse<CustomerOrderHistoryPageDto>>;

public sealed class GetCustomerOrderHistoryQueryHandler(ApplicationDbContext context,
    ICurrentUserService currentUser, ITenantClock clock)
    : IQueryHandler<GetCustomerOrderHistoryQuery, ApiResponse<CustomerOrderHistoryPageDto>>
{
    private const int PageSize = 20;
    public async Task<ApiResponse<CustomerOrderHistoryPageDto>> Handle(GetCustomerOrderHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        // Owner scope precedes sorting and the inner bound. A cursor only navigates that scope;
        // no operation capability, private contact, provider identifier or catalogue graph is read.
        var account = await CustomerAccountAccess.Current(context, currentUser).AsNoTracking()
            .Select(user => new
            {
                Items = context.Orders.Where(order => order.UserId == user.Id && order.UserId == owner && !order.IsDeleted
                        && (!query.BeforeOrderDate.HasValue || order.OrderDate < query.BeforeOrderDate.Value
                            || (order.OrderDate == query.BeforeOrderDate.Value && order.Id.CompareTo(query.BeforeOrderId!.Value) > 0)))
                    .OrderByDescending(order => order.OrderDate).ThenBy(order => order.Id).Take(PageSize + 1)
                    .Select(order => new
                    {
                        order.Id,
                        order.OrderNumber,
                        order.Type,
                        order.OrderDate,
                        order.Status,
                        order.PaymentStatus,
                        Total = order.Total + order.BillChargeAmount - order.BillingCreditAmount,
                        Currency = context.OrderBillingSnapshots.Where(snapshot => snapshot.OrderId == order.Id)
                            .Select(snapshot => snapshot.Currency).SingleOrDefault()
                    }).ToList()
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Customer account unavailable.");
        // Legacy orders without immutable currency remain visible. Never label their amounts
        // with today's restaurant currency or infer a historical settlement from current config.
        var items = account.Items.Take(PageSize).Select(order =>
        {
            var currency = CurrencyCode.IsValid(order.Currency) ? CurrencyCode.Normalize(order.Currency) : null;
            return new CustomerOrderHistoryItemDto(order.Id, order.OrderNumber, order.Type, order.OrderDate,
                order.Status, order.PaymentStatus, currency, currency is null ? null : order.Total);
        }).ToArray();
        CustomerOrderHistoryCursorDto? next = account.Items.Count > PageSize
            ? new(items[^1].OrderDate, items[^1].OrderId) : null;
        return ApiResponse<CustomerOrderHistoryPageDto>.SuccessWithData(new(1, clock.TimeZone.Id, items, next));
    }
}
