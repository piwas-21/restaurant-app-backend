using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutAtomicityTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    [Fact]
    public async Task Evidence_write_failure_after_tender_save_rolls_back_money_ticket_and_journal_together()
    {
        await SeedAsync();
        var lease = await WithCheckoutAsync((_, _, leases) => leases.AcquireAsync(Session, Proof, CancellationToken.None));
        var fault = new EvidenceFailure();
        await FluentActions.Invoking(() => WithCheckoutAsync(async (_, _, leases) =>
        {
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        }, interceptors: [fault])).Should().ThrowAsync<IOException>();
        fault.Triggered.Should().BeTrue();
        await using var read = Database.CreateContext();
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        (await read.Orders.SingleAsync()).TotalPaid.Should().Be(0);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        var journal = await read.Set<RestaurantSystem.Domain.Entities.CustomerOrderCheckoutJournal>().SingleAsync();
        journal.ProviderSessionId.Should().BeNull();
        journal.LeaseId.Should().Be(lease.Id);
        // The untouched original lease remains usable after the rolled-back financial phase.
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        });
        (await read.Orders.AsNoTracking().SingleAsync()).TotalPaid.Should().Be(12.34m);
    }

    [Fact]
    public async Task A_corrupted_retained_tender_refuses_replay_instead_of_minting_or_repairing_money()
    {
        await SeedAsync();
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        });
        await using (var write = Database.CreateContext())
            await write.OrderPayments.ExecuteUpdateAsync(update => update.SetProperty(value => value.Amount, 12m));
        await FluentActions.Invoking(() => WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        })).Should().ThrowAsync<ConflictException>();
        await using var read = Database.CreateContext();
        (await read.OrderPayments.SingleAsync()).Amount.Should().Be(12m);
        (await read.OrderPayments.CountAsync()).Should().Be(1);
    }

    private sealed class EvidenceFailure : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE customer_order_checkout_journals", StringComparison.Ordinal)
                && command.CommandText.Contains("last_verified_at", StringComparison.Ordinal))
            {
                Triggered = true;
                throw new IOException("injected failure after financial persistence");
            }
            return ValueTask.FromResult(result);
        }
    }
}
