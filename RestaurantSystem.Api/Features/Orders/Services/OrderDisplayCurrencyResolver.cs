using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Order-currency resolution for every <see cref="OrderDto"/> surface (POS plan C18):
/// the first payment tender that carries a <see cref="OrderPayment.Currency"/>, else the
/// tenant's declared <see cref="RestaurantInfo.Currency"/>, else null. A consumer receiving
/// null must not invent a label — an invented one is how receipts printed CHF on a EUR
/// tenant's paper in the first place.
/// </summary>
/// <remarks>
/// <para>
/// Precedence details, deliberately explicit:
/// <list type="bullet">
/// <item>ANY tender status counts. A Processing or Refunded Stripe capture still names the
/// currency the money moved in; filtering by status would drop a EUR order to the tenant
/// default mid-refund and relabel its own receipt.</item>
/// <item>"First" is the earliest <see cref="OrderPayment.PaymentDate"/> (id as a stable
/// tiebreak). A partial tender then a different-currency top-up is a pricing incident, not
/// something a display label can express — the earliest wins deterministically.</item>
/// <item>Blank tender values don't count: cash has no currency of its own, and the column is
/// null on every tender created before online payments existed.</item>
/// </list>
/// </para>
/// <para>
/// Registered SCOPED and caching the tenant answer on first use: the staff list maps one
/// <see cref="OrderDto"/> per row, and without the cache the singleton lookup would run once
/// per row (the N+1 the C18 slice was told not to introduce). The query only runs when no
/// payment on any order mapped in this request carried a currency — the settled-ticket case
/// costs nothing. Tenant-wide it is one row; a join per order would buy nothing.
/// </para>
/// </remarks>
public class OrderDisplayCurrencyResolver : IOrderDisplayCurrencyResolver
{
    private readonly ApplicationDbContext _context;

    private string? _tenantCurrency;
    private bool _tenantCurrencyResolved;

    public OrderDisplayCurrencyResolver(ApplicationDbContext context)
    {
        _context = context;
    }

    public string? Resolve(Order order)
    {
        var fromTender = order.Payments?
            .Where(p => !string.IsNullOrWhiteSpace(p.Currency))
            .OrderBy(p => p.PaymentDate)
            .ThenBy(p => p.Id)
            .FirstOrDefault()?.Currency;

        if (fromTender is not null)
        {
            return fromTender;
        }

        if (!_tenantCurrencyResolved)
        {
            _tenantCurrency = _context.RestaurantInfo
                .AsNoTracking()
                .Select(r => r.Currency)
                .FirstOrDefault();
            _tenantCurrencyResolved = true;
        }

        return _tenantCurrency;
    }
}
