using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerOrderCheckoutEvidenceWriter(ApplicationDbContext context,
    ICustomerOrderCheckoutLedger ledger) : ICustomerOrderCheckoutEvidenceWriter
{
    public async Task<CheckoutSessionStatus?> WriteAsync(CustomerOrderCheckoutJournal journal, CustomerOrderCheckoutLease lease,
        StripeCheckoutSession evidence, DateTime now, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native evidence writes require the original order lock and transaction.");
        if (journal.LeaseId != lease.Id || journal.LeaseExpiresAt is null || journal.LeaseExpiresAt <= now
            || journal.OrderId != lease.Request.OrderId || journal.ProviderSessionId != lease.ProviderSessionId
            || CustomerOrderCheckoutReplay.Hash(lease.Request) != journal.CreatePayloadHash)
            throw Pending();
        var request = CustomerOrderCheckoutReplay.Read(journal, now, forCreate: false);
        NativeCheckoutSessionEvidence.Require(request, evidence);
        if (journal.ProviderSessionId is not null && journal.ProviderSessionId != evidence.Id) throw Pending();
        var result = await ledger.ApplyAsync(journal, evidence, now, cancellationToken);
        await context.Set<CustomerOrderCheckoutJournal>().Where(value => value.OrderId == journal.OrderId)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.ProviderSessionId, evidence.Id)
                .SetProperty(value => value.LastVerifiedAt, now)
                .SetProperty(value => value.LeaseId, (Guid?)null)
                .SetProperty(value => value.LeaseExpiresAt, (DateTime?)null), cancellationToken);
        return result;
    }

    private static ConflictException Pending() => new("The original payment is pending reconciliation. Do not start another payment.");
}
