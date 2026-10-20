using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutSettlementTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    private Task<T> WithLeaseAsync<T>(Func<CustomerOrderCheckoutLeaseStore, CustomerOrderCheckoutLease, Task<T>> action) =>
        WithCheckoutAsync(async (_, _, leases) => await action(leases,
            await leases.AcquireAsync(Session, Proof, CancellationToken.None)));

    [Fact]
    public async Task Canonical_paid_evidence_commits_one_original_tender_and_exact_replays_do_not_change_money_or_version()
    {
        var id = await SeedAsync();
        var evidence = await WithLeaseAsync(async (leases, lease) =>
        {
            var paid = Paid(lease);
            (await leases.RecordAsync(Session, Proof, lease, paid, CancellationToken.None)).Should().BeFalse();
            return paid;
        });
        await using var read = Database.CreateContext();
        var first = await read.Orders.AsNoTracking().SingleAsync();
        first.TotalPaid.Should().Be(12.34m);
        first.RemainingAmount.Should().Be(0m);
        first.PaymentStatus.Should().Be(PaymentStatus.Completed);
        first.Status.Should().Be(OrderStatus.Pending, "payment never grants restaurant acceptance");
        var tender = await read.OrderPayments.AsNoTracking().SingleAsync();
        tender.Status.Should().Be(PaymentStatus.Completed);
        tender.Amount.Should().Be(12.34m);
        tender.Currency.Should().Be("chf");
        tender.TransactionId.Should().Be("pi_native_settlement_fixture");
        var ticket = await read.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        ticket.OrderId.Should().Be(id);
        ticket.OrderPaymentId.Should().Be(tender.Id);
        ticket.Status.Should().Be(CheckoutSessionStatus.Completed);
        ticket.AmountReceivedMinor.Should().Be(1234);
        ticket.ReconciledAt.Should().Be(Clock.Now.UtcDateTime);
        await WithLeaseAsync(async (leases, lease) =>
        {
            await leases.RecordAsync(Session, Proof, lease, evidence, CancellationToken.None);
            return true;
        });
        var after = await read.Orders.AsNoTracking().SingleAsync();
        after.Version.Should().Be(first.Version);
        after.TotalPaid.Should().Be(12.34m);
        (await read.OrderPayments.CountAsync()).Should().Be(1);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("open", "unpaid")]
    [InlineData("complete", "unpaid")]
    [InlineData("complete", "future-payment")]
    [InlineData("future-state", "unpaid")]
    public async Task Delayed_and_unknown_outcomes_never_book_money_or_release_the_original_processing_intent(string status, string payment)
    {
        await SeedAsync();
        await WithLeaseAsync(async (leases, lease) =>
        {
            var evidence = CustomerOrderCheckoutLeaseTests.Evidence(lease) with { Status = status, PaymentStatus = payment };
            await leases.RecordAsync(Session, Proof, lease, evidence, CancellationToken.None);
            return true;
        });
        await using var read = Database.CreateContext();
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        (await read.Orders.SingleAsync()).TotalPaid.Should().Be(0);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await read.Set<CustomerOrderCheckoutJournal>().SingleAsync()).ProviderSessionId.Should().Be("cs_native_canonical_fixture");
    }

    [Fact]
    public async Task Explicit_unpaid_expiry_is_atomic_and_leaves_the_order_for_explicit_customer_or_staff_action()
    {
        await SeedAsync();
        await WithLeaseAsync(async (leases, lease) =>
        {
            var expired = CustomerOrderCheckoutLeaseTests.Evidence(lease) with { Status = "expired", Url = null };
            await leases.RecordAsync(Session, Proof, lease, expired, CancellationToken.None);
            return true;
        });
        await using var read = Database.CreateContext();
        var order = await read.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Pending);
        order.TotalPaid.Should().Be(0);
        var tender = await read.OrderPayments.SingleAsync();
        tender.Status.Should().Be(PaymentStatus.Failed);
        var ticket = await read.OrderCheckoutSessions.SingleAsync();
        ticket.Status.Should().Be(CheckoutSessionStatus.Expired);
        ticket.AmountReceivedMinor.Should().BeNull();
        ticket.OrderPaymentId.Should().BeNull();
        (await read.Set<CustomerOrderCheckoutJournal>().SingleAsync()).LeaseId.Should().BeNull();
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cash")]
    public async Task Canonical_capture_is_retained_after_other_order_changes_and_never_undoes_them(string scenario)
    {
        await SeedAsync();
        await WithLeaseAsync(async (leases, lease) =>
        {
            await using (var write = Database.CreateContext())
            {
                var order = await write.Orders.SingleAsync();
                if (scenario == "closed") order.Status = OrderStatus.Cancelled;
                else
                {
                    write.OrderPayments.Add(new()
                    {
                        OrderId = order.Id,
                        PaymentMethod = PaymentMethod.Cash,
                        Status = PaymentStatus.Completed,
                        Amount = 12.34m,
                        CreatedBy = "cash-fixture"
                    });
                    order.TotalPaid = 12.34m;
                    order.PaymentStatus = PaymentStatus.Completed;
                }
                await write.SaveChangesAsync();
            }
            await leases.RecordAsync(Session, Proof, lease, Paid(lease), CancellationToken.None);
            return true;
        });
        await using var read = Database.CreateContext();
        var current = await read.Orders.SingleAsync();
        current.TotalPaid.Should().Be(scenario == "cash" ? 24.68m : 12.34m);
        current.PaymentStatus.Should().Be(scenario == "cash" ? PaymentStatus.Overpaid : PaymentStatus.Completed);
        current.Status.Should().Be(scenario == "closed" ? OrderStatus.Cancelled : OrderStatus.Pending);
    }

    [Theory]
    [InlineData("missing-intent")]
    [InlineData("invalid-intent")]
    [InlineData("paid-open")]
    [InlineData("paid-expired")]
    public async Task Contradictory_or_missing_terminal_evidence_rolls_back_the_entire_financial_phase(string scenario)
    {
        await SeedAsync();
        await WithLeaseAsync(async (leases, lease) =>
        {
            var paid = Paid(lease);
            var evidence = scenario switch
            {
                "missing-intent" => paid with { PaymentIntentId = null },
                "invalid-intent" => paid with { PaymentIntentId = "not-a-payment-intent" },
                "paid-open" => paid with { Status = "open" },
                "paid-expired" => paid with { Status = "expired" },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            await FluentActions.Invoking(() => leases.RecordAsync(Session, Proof, lease, evidence, CancellationToken.None))
                .Should().ThrowAsync<ConflictException>();
            return true;
        });
        await using var read = Database.CreateContext();
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        var journal = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        journal.ProviderSessionId.Should().BeNull();
        journal.LastVerifiedAt.Should().BeNull();
        journal.LeaseId.Should().NotBeNull();
    }

    [Theory]
    [InlineData("processing", 0)]
    [InlineData("requires_action", 0)]
    [InlineData("succeeded", 1200)]
    public async Task A_paid_session_without_full_canonical_intent_capture_stays_pending(string status, long received)
    {
        await SeedAsync();
        await WithLeaseAsync(async (leases, lease) =>
        {
            var paid = Paid(lease);
            await leases.RecordAsync(Session, Proof, lease, paid with
            {
                NativeIntentEvidence = paid.NativeIntentEvidence! with
                { Status = status, AmountReceivedMinor = received }
            }, CancellationToken.None);
            return true;
        });
        await using var read = Database.CreateContext();
        (await read.Orders.SingleAsync()).TotalPaid.Should().Be(0);
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
    }

    internal static StripeCheckoutSession Paid(CustomerOrderCheckoutLease lease) =>
        CustomerOrderCheckoutLeaseTests.Evidence(lease) with
        {
            Status = "complete",
            PaymentStatus = "paid",
            PaymentIntentId = "pi_native_settlement_fixture",
            Url = null,
            NativeIntentEvidence = new StripePaymentIntent
            {
                Id = "pi_native_settlement_fixture",
                Status = "succeeded",
                AmountMinor = 1234,
                AmountReceivedMinor = 1234,
                ApplicationFeeMinor = lease.Request.ApplicationFeeMinor,
                Currency = "chf",
                ProviderContext = new("acct_native_fixture", false),
                Metadata = lease.Request.NativeRouting!.Metadata()
            }
        };
}
