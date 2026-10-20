using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Short proof/owner-bound transactions; an expired worker cannot publish another worker's result.</summary>
public sealed class CustomerOrderCheckoutLeaseStore(ApplicationDbContext context, ICustomerOrderCheckoutJournalStore journals,
    ICustomerOrderCheckoutCreationPolicy creation, ICustomerOrderCheckoutEvidenceWriter evidenceWriter,
    ICustomerOrderCheckoutOwnerScope owners) : ICustomerOrderCheckoutLeaseStore
{
    public static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(1);

    public async Task<CustomerOrderCheckoutLease> AcquireAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        RequireCleanContext();
        await journals.FreezeAsync(sessionId, proof, cancellationToken);
        return await owners.RunAsync(sessionId, proof, async (journal, order, now) =>
        {
            if (journal.LeaseExpiresAt > now) throw Pending();
            var request = CustomerOrderCheckoutReplay.Read(journal, now, forCreate: journal.ProviderSessionId is null);
            if (journal.ProviderSessionId is null)
            {
                var charge = await creation.ReadAsync(order, cancellationToken);
                if (charge.Amount.Minor != journal.AmountMinor || charge.Amount.Currency != journal.Currency)
                    throw Pending();
            }
            var id = Guid.NewGuid();
            await context.Set<CustomerOrderCheckoutJournal>().Where(value => value.OrderId == journal.OrderId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.LeaseId, id)
                    .SetProperty(value => value.LeaseExpiresAt, now.Add(LeaseLifetime)), cancellationToken);
            return new CustomerOrderCheckoutLease(id, request, journal.ProviderSessionId, now.Add(LeaseLifetime), journal.MaximumCreateRetryAt);
        }, cancellationToken);
    }

    public Task<bool> RecordAsync(string sessionId, CustomerOrderRecoveryDto proof, CustomerOrderCheckoutLease lease,
        StripeCheckoutSession evidence, CancellationToken cancellationToken)
    {
        return owners.RunAsync(sessionId, proof, async (journal, order, now) =>
        {
            await evidenceWriter.WriteAsync(journal, lease, evidence, now, cancellationToken);
            return await MayContinueAsync(journal, order, evidence, now, cancellationToken);
        }, cancellationToken);
    }

    private async Task<bool> MayContinueAsync(CustomerOrderCheckoutJournal journal, Order order,
        StripeCheckoutSession evidence, DateTime now, CancellationToken cancellationToken)
    {
        if (!evidence.IsOpen || evidence.PaymentStatus != "unpaid" || now >= journal.ExpiresAt) return false;
        try
        {
            var charge = await creation.ReadAsync(order, cancellationToken);
            return charge.Amount.Minor == journal.AmountMinor && charge.Amount.Currency == journal.Currency;
        }
        catch (Exception error) when (error is NotFoundException or BadRequestException or ConflictException)
        {
            // Preserve canonical financial evidence even if permission/money changed during I/O.
            // That evidence never authorizes opening the old payable page in the changed state.
            return false;
        }
    }

    private void RequireCleanContext()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native payment phases require an isolated clean context.");
    }

    private static ConflictException Pending() => new("The original payment is pending reconciliation. Do not start another payment.");
}
