using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentAccountReader(ApplicationDbContext context, CustomMediator mediator,
    ICustomerCheckoutGuard owners, IAccountPaymentAccountReader accounts,
    ITableGuestParticipantPaymentAuthorization participants) : ICustomerTablePaymentAccountReader
{
    public async Task<CustomerTablePaymentAccountDto> ReadAsync(string sessionId, string? participantToken,
        int? schemaVersion, Guid tenantId, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        if (schemaVersion != 1 || tenantId == Guid.Empty || serviceSessionId == Guid.Empty) throw Unavailable();
        var owner = owners.EnsureOwner(sessionId);
        var currency = await RequireCurrentAsync(sessionId, owner, tenantId, cancellationToken);
        var participant = await participants.AuthorizeActiveAsync(serviceSessionId, participantToken, cancellationToken);
        // The shared reader independently requires a live participant, a consistent ledger, and its
        // configured display limits. Its legacy debt scans are not bounded by this response contract.
        var account = await accounts.GetGuestAsync(serviceSessionId, participantToken, cancellationToken);
        CustomerTablePaymentAccountShape.Require(account, serviceSessionId, currency);
        if (currency != await RequireCurrentAsync(sessionId, owner, tenantId, cancellationToken)) throw Unavailable();
        if (participant != await participants.AuthorizeActiveAsync(serviceSessionId, participantToken, cancellationToken))
            throw Unavailable();
        return new(1, tenantId, account);
    }

    private async Task<string> RequireCurrentAsync(string sessionId, Guid? originalOwner, Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (owners.EnsureOwner(sessionId) != originalOwner
            || originalOwner is { } owner && !await CustomerAccountAccess.ForOwner(context, owner)
                .AsNoTracking().AnyAsync(cancellationToken)) throw Unavailable();
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap is null || bootstrap.TenantId != tenantId
            || bootstrap.Capabilities.GetValueOrDefault("table-visits") != "enabled"
            || !bootstrap.Flags.GetValueOrDefault("tableAccountPaymentsV1")
            || originalOwner.HasValue && bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled"
            || string.IsNullOrWhiteSpace(bootstrap.Currency)) throw Unavailable();
        return bootstrap.Currency;
    }

    private static NotFoundException Unavailable() => new("Customer table payment account is unavailable.");
}
