using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Short proof/owner-bound transactions; an expired worker cannot publish another worker's result.</summary>
public sealed class CustomerOrderCheckoutLeaseStore(ApplicationDbContext context, ICustomerCheckoutGuard guard,
    ICustomerOrderOperationStore operations, ICustomerOrderCheckoutJournalStore journals,
    ICustomerOrderCheckoutCreationPolicy creation, TimeProvider clock) : ICustomerOrderCheckoutLeaseStore
{
    public static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(1);

    public async Task<CustomerOrderCheckoutLease> AcquireAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        RequireCleanContext();
        await journals.FreezeAsync(sessionId, proof, cancellationToken);
        return await RunAsync(sessionId, proof, async (journal, order, now) =>
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
        return RunAsync(sessionId, proof, async (journal, order, now) =>
        {
            if (journal.LeaseId != lease.Id || journal.LeaseExpiresAt is null || journal.LeaseExpiresAt <= now
                || journal.OrderId != lease.Request.OrderId || journal.ProviderSessionId != lease.ProviderSessionId
                || CustomerOrderCheckoutReplay.Hash(lease.Request) != journal.CreatePayloadHash)
                throw Pending();
            var request = CustomerOrderCheckoutReplay.Read(journal, now, forCreate: false);
            NativeCheckoutSessionEvidence.Require(request, evidence);
            if (journal.ProviderSessionId is not null && journal.ProviderSessionId != evidence.Id) throw Pending();
            await context.Set<CustomerOrderCheckoutJournal>().Where(value => value.OrderId == journal.OrderId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.ProviderSessionId, evidence.Id)
                    .SetProperty(value => value.LastVerifiedAt, now)
                    .SetProperty(value => value.LeaseId, (Guid?)null)
                    .SetProperty(value => value.LeaseExpiresAt, (DateTime?)null), cancellationToken);
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

    private async Task<T> RunAsync<T>(string sessionId, CustomerOrderRecoveryDto proof,
        Func<CustomerOrderCheckoutJournal, Order, DateTime, Task<T>> work, CancellationToken cancellationToken)
    {
        RequireCleanContext();
        if (proof.SchemaVersion != 1) throw new BadRequestException("Unsupported native payment schema.");
        var owner = guard.EnsureOwner(sessionId);
        var operation = await operations.RecoverAsync(sessionId, proof.OperationId, proof.PayloadHash, cancellationToken);
        if (operation.Outcome != CustomerOrderOperationOutcome.Applied || operation.OrderId is not { } orderId)
            throw Unavailable();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
            if (owner is { } userId)
                await CustomerAccountWriteGuard.AcquireAsync(context, userId, true, cancellationToken);
            var order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == orderId
                && value.UserId == owner && !value.IsDeleted, cancellationToken) ?? throw Unavailable();
            if (order.ServiceSessionId is not null) throw Unavailable();
            var journal = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == orderId && value.OperationId == proof.OperationId,
                    cancellationToken) ?? throw Unavailable();
            var result = await work(journal, order, clock.GetUtcNow().UtcDateTime);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private void RequireCleanContext()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native payment phases require an isolated clean context.");
    }

    private static ConflictException Pending() => new("The original payment is pending reconciliation. Do not start another payment.");
    private static NotFoundException Unavailable() => new("Native payment operation unavailable.");
}
