using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentAccess(ApplicationDbContext context, ICustomerCheckoutGuard owners,
    CustomMediator mediator, IOptions<CustomerMobileSettings> settings,
    IOptions<CustomerHostedCheckoutSettings> hosted, IGuestAccountPaymentPolicy policy) : ICustomerTablePaymentAccess
{
    public CustomerTablePaymentOwner Capture(string sessionId, int schemaVersion, Guid tenantId)
    {
        if (schemaVersion != 1 || tenantId == Guid.Empty || tenantId != settings.Value.TenantId) throw Unavailable();
        var customer = owners.EnsureOwner(sessionId);
        var session = CustomerBasketOperationHash.Session(sessionId);
        return new(session, customer, CustomerTablePaymentHashes.Owner(session, customer));
    }

    public async Task<string> RequireNewAsync(CustomerTablePaymentOwner owner, Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (Capture(owner.SessionId.ToString("D"), 1, tenantId) != owner
            || owner.CustomerId is { } customer && !await CustomerAccountAccess.ForOwner(context, customer)
                .AsNoTracking().AnyAsync(cancellationToken)) throw Unavailable();
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap is null || bootstrap.TenantId != tenantId || bootstrap.Lifecycle != "active"
            || bootstrap.Capabilities.GetValueOrDefault("table-visits") != "enabled"
            || !settings.Value.EnabledCapabilities.Contains("table-payments", StringComparer.Ordinal)
            || owner.CustomerId.HasValue && bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled"
            || !hosted.Value.Enabled || !hosted.Value.IsValid() || string.IsNullOrWhiteSpace(bootstrap.Currency))
            throw Unavailable();
        policy.RequireNewPayment();
        return bootstrap.Currency;
    }

    private static NotFoundException Unavailable() => new("Customer table payment preparation is unavailable.");
}
