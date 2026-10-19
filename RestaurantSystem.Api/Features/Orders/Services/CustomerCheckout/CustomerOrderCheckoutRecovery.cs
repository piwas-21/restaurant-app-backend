using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Provider I/O happens between committed lease/evidence phases, always on the original request.</summary>
public sealed class CustomerOrderCheckoutRecovery(ICustomerOrderCheckoutLeaseStore leases,
    IStripeCheckoutClient provider, IStripeGateway gateway, IOptions<StripeSettings> stripe, TimeProvider clock) : ICustomerOrderCheckoutRecovery
{
    public async Task<StripeCheckoutSession> RecoverAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        var lease = await leases.AcquireAsync(sessionId, proof, cancellationToken);
        var request = lease.Request;
        var routing = request.NativeRouting
            ?? throw new InvalidOperationException("Native recovery requires frozen routing.");
        routing.RequireCurrent(gateway, stripe.Value, request.OrderId);
        var id = lease.ProviderSessionId;
        if (id is null)
        {
            if (clock.GetUtcNow().UtcDateTime >= lease.MaximumCreateRetryAt) throw Pending();
            RequireLiveLease(lease);
            var created = await provider.CreateAsync(request, cancellationToken);
            NativeCheckoutSessionEvidence.Require(request, created);
            id = created.Id;
        }
        // Creation responses and browser returns are not current canonical state. A missing session
        // or provider failure keeps the durable request and lease; no failure path rotates identity.
        routing.RequireCurrent(gateway, stripe.Value, request.OrderId);
        RequireLiveLease(lease);
        var evidence = await provider.GetAsync(id, cancellationToken)
            ?? throw new ConflictException("The original payment requires provider reconciliation.");
        NativeCheckoutSessionEvidence.Require(request, evidence);
        if (evidence.Id != id) throw new ConflictException("The provider returned a different payment session.");
        var mayContinue = await leases.RecordAsync(sessionId, proof, lease, evidence, cancellationToken);
        return mayContinue ? evidence : evidence with { Url = null };
    }

    private void RequireLiveLease(CustomerOrderCheckoutLease lease)
    {
        if (clock.GetUtcNow().UtcDateTime >= lease.LeaseExpiresAt) throw Pending();
    }

    private static ConflictException Pending() => new("The original payment requires reconciliation before continuing.");
}
