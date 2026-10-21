using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundQuotes(ApplicationDbContext context, ICustomerCheckoutGuard checkout,
    ICustomerCheckoutDraft draft, ICustomerTableRoundAccess access) : ICustomerTableRoundQuotes
{
    public async Task<CustomerTableRoundReview> ReviewAsync(string sessionId, CustomerTableRoundOwner owner,
        CustomerTableRoundIntentDto request, TableServiceSession session, CancellationToken cancellationToken)
    {
        await access.RequireNewAsync(owner, cancellationToken);
        var input = new CustomerCheckoutReviewDto
        {
            SchemaVersion = 1,
            ExpectedBasketId = request.ExpectedBasketId,
            ExpectedPurchaseFingerprint = request.ExpectedPurchaseFingerprint,
            Type = OrderType.DineIn,
            PointsToRedeem = 0,
            Tip = 0m
        };
        var owned = await checkout.ReviewAsync(sessionId, owner.CustomerId, input, cancellationToken);
        var currency = await OrderNativeAcceptedCurrency.ResolveForAcceptanceAsync(context, session.Currency, cancellationToken);
        if (currency is null || owned.Currency != currency)
            throw new ConflictException("The table and catalogue currency differ. Refresh before adding a round.");
        // Table rounds belong to the shared visit. A customer's basket does not grant personal
        // discounts, points or a tender to this guest order; review the actual shared-table price.
        var shared = owned with { UserId = null, PaymentMethods = [] };
        var prepared = await draft.PrepareAsync(shared, input, cancellationToken);
        var label = await context.Tables.AsNoTracking().Where(value => value.Id == session.TableId)
            .Select(value => value.TableNumber).SingleAsync(cancellationToken);
        return new(shared, input, prepared, CustomerCheckoutProjection.Create(shared, input, prepared.Order), label);
    }
}
