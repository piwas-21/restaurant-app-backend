using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundAccess(ApplicationDbContext context, ICustomerCheckoutGuard checkout,
    IOptions<CustomerMobileSettings> settings, CustomMediator mediator, IOrderAvailabilityGuard availability,
    TimeProvider clock) : ICustomerTableRoundAccess
{
    public CustomerTableRoundOwner Capture(string sessionId, int schemaVersion, Guid tenantId)
    {
        if (schemaVersion != 1 || tenantId == Guid.Empty || tenantId != settings.Value.TenantId) throw Unavailable();
        var customer = checkout.EnsureOwner(sessionId);
        var session = CustomerBasketOperationHash.Session(sessionId);
        return new(session, customer, CustomerTableRoundHashes.Owner(session, customer));
    }
    public async Task LockCustomerAsync(CustomerTableRoundOwner owner, CancellationToken cancellationToken)
    {
        if (owner.CustomerId is { } customer)
            await CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken);
    }
    public void RequireOriginal(string sessionId, int schemaVersion, Guid tenantId, CustomerTableRoundOwner owner)
    {
        if (Capture(sessionId, schemaVersion, tenantId) != owner) throw Unavailable();
    }
    public async Task<CustomerMobileBootstrapDto> RequireNewAsync(CustomerTableRoundOwner owner, CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerBasketAvailability.ReadOrderingAsync(mediator, cancellationToken);
        if (bootstrap.Capabilities.GetValueOrDefault("table-visits") != "enabled"
            || !bootstrap.Flags.GetValueOrDefault("tableGuestVisitsV1")
            || owner.CustomerId.HasValue && bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled")
            throw Unavailable();
        await availability.EnsureAvailableAsync(OrderType.DineIn, cancellationToken);
        return bootstrap;
    }
    public async Task RevalidateAsync(CustomerTableRoundOwner owner, CustomerTableRoundBinding binding, bool acceptedRound, CancellationToken cancellationToken)
    {
        await LockCustomerAsync(owner, cancellationToken);
        var bootstrap = await RequireNewAsync(owner, cancellationToken);
        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == binding.ServiceSessionId, cancellationToken);
        if (session is null || session.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue
            || session.TableId != binding.TableId || session.AccountRevision < binding.ExpectedAccountRevision + (acceptedRound ? 1 : 0)
            || await OrderNativeAcceptedCurrency.ResolveForAcceptanceAsync(context, session.Currency, cancellationToken) != binding.Currency
            || bootstrap.Currency != binding.Currency) throw Unavailable();
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await context.Set<TableGuestParticipant>().AsNoTracking().AnyAsync(value =>
                value.Id == binding.ParticipantId && value.ServiceSessionId == binding.ServiceSessionId
                && value.TokenHash == binding.ParticipantTokenHash && value.RevokedAt == null && value.ExpiresAt > now,
                cancellationToken)) throw Unavailable();
    }
    private static NotFoundException Unavailable() => new("Customer table round unavailable. Refresh this restaurant.");
}
