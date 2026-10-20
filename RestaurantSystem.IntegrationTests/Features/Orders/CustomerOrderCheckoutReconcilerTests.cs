using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.IntegrationTests.Features.Payments;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutReconcilerTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    private readonly Mock<IStripeCheckoutClient> _provider = new(MockBehavior.Strict);
    private readonly Mock<IStripeGateway> _gateway = new(MockBehavior.Strict);
    private readonly Mock<ICustomerOrderCheckoutEffects> _effects = new(MockBehavior.Strict);
    private readonly StripeSettings _stripe = new() { PlatformApiKey = "rk_test_native_fixture" }; // pragma: allowlist secret

    private async Task<CustomerOrderCheckoutLease> PrimeAsync(Guid? owner = null)
    {
        await SeedAsync(owner);
        return await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutLeaseTests.Evidence(lease), CancellationToken.None);
            return lease;
        }, owner);
    }

    private CustomerOrderCheckoutReconciler Build(ApplicationDbContext context, ILogger<CustomerOrderCheckoutReconciler>? logger = null)
    {
        _gateway.SetupGet(value => value.IsConfigured).Returns(true);
        _gateway.SetupGet(value => value.ConnectedAccountId).Returns("acct_native_fixture");
        return new(context, EvidenceWriter(context),
            new CustomerOrderCheckoutProviderReader(_provider.Object, _gateway.Object, Options.Create(_stripe)),
            Options.Create(Settings), Clock, _effects.Object,
            logger ?? NullLogger<CustomerOrderCheckoutReconciler>.Instance);
    }

    private void Respond(StripeCheckoutSession remote)
    {
        _provider.Setup(value => value.GetAsync(remote.Id, It.IsAny<CancellationToken>())).ReturnsAsync(remote);
        if (remote.PaymentIntentId is { } id)
            _provider.Setup(value => value.GetPaymentIntentAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(remote.NativeIntentEvidence);
    }

    [Fact]
    public async Task Recorded_capture_settles_without_app_return_and_after_owner_erasure_or_creation_withdrawal()
    {
        var owner = Guid.NewGuid();
        var lease = await PrimeAsync(owner);
        await using (var write = Database.CreateContext())
        {
            await using var transaction = await write.Database.BeginTransactionAsync();
            await new RetainedCustomerDataScrubber(write).ScrubAsync(owner, CancellationToken.None);
            await write.Users.Where(value => value.Id == owner).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        Settings.Enabled = false;
        Clock.Now = Clock.Now.AddDays(2);
        var paid = CustomerOrderCheckoutSettlementTests.Paid(lease);
        Respond(paid);
        await using var context = Database.CreateContext();
        var worker = Build(context);
        _provider.Setup(value => value.GetAsync(paid.Id, It.IsAny<CancellationToken>()))
            .Callback(() => context.Database.CurrentTransaction.Should().BeNull()).ReturnsAsync(paid);
        _effects.Setup(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>()))
            .Returns(async () => { context.Database.CurrentTransaction.Should().BeNull(); await CompleteEffectsAsync(); });
        var result = await worker.RunAsync(10, CancellationToken.None);
        result.Examined.Should().Be(1);
        result.Settled.Should().Be(1);
        result.Failures.Should().Be(0);
        await using var read = Database.CreateContext();
        var order = await read.Orders.SingleAsync();
        order.UserId.Should().BeNull();
        order.TotalPaid.Should().Be(12.34m);
        (await read.OrderPayments.SingleAsync()).TransactionId.Should().Be("pi_native_settlement_fixture");
        (await worker.RunAsync(10, CancellationToken.None)).Examined.Should().Be(0);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _effects.Verify(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delayed_payment_remains_processing_then_only_canonical_paid_intent_settles_it()
    {
        var lease = await PrimeAsync();
        var paid = CustomerOrderCheckoutSettlementTests.Paid(lease);
        var pending = paid with
        {
            PaymentStatus = "unpaid",
            NativeIntentEvidence = paid.NativeIntentEvidence! with
            { Status = "processing", AmountReceivedMinor = 0 }
        };
        Respond(pending);
        await using var context = Database.CreateContext();
        var worker = Build(context);
        var first = await worker.RunAsync(10, CancellationToken.None);
        first.Settled.Should().Be(0);
        first.Failures.Should().Be(0);
        (await context.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        Clock.Now = Clock.Now.AddSeconds(1);
        Respond(paid);
        _effects.Setup(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>())).Returns(CompleteEffectsAsync);
        var second = await worker.RunAsync(10, CancellationToken.None);
        second.Settled.Should().Be(1);
        second.Failures.Should().Be(0);
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Completed);
        (await worker.RunAsync(10, CancellationToken.None)).Examined.Should().Be(0);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("requires_payment_method")]
    public async Task Completed_unpaid_session_with_canonical_failed_zero_received_intent_releases_only_the_processing_tender(string status)
    {
        var lease = await PrimeAsync();
        var paid = CustomerOrderCheckoutSettlementTests.Paid(lease);
        Respond(paid with
        {
            PaymentStatus = "unpaid",
            NativeIntentEvidence = paid.NativeIntentEvidence! with
            { Status = status, AmountReceivedMinor = 0 }
        });
        await using var context = Database.CreateContext();
        var result = await Build(context).RunAsync(10, CancellationToken.None);
        result.Failures.Should().Be(0);
        result.Settled.Should().Be(0);
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Failed);
        (await context.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.Pending);
        (await context.OrderCheckoutSessions.AsNoTracking().SingleAsync()).Status.Should().Be(CheckoutSessionStatus.Failed);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _effects.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("mode")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("operation")]
    [InlineData("fee")]
    [InlineData("missing")]
    public async Task Intent_identity_mismatches_and_missing_canonical_reads_cannot_capture_or_release_money(string scenario)
    {
        var lease = await PrimeAsync();
        var paid = CustomerOrderCheckoutSettlementTests.Paid(lease);
        var intent = paid.NativeIntentEvidence!;
        var invalid = scenario switch
        {
            "account" => intent with { ProviderContext = new("acct_foreign", false) },
            "mode" => intent with { ProviderContext = new("acct_native_fixture", true) },
            "currency" => intent with { Currency = "eur" },
            "amount" => intent with { AmountMinor = 1200 },
            "operation" => intent with { Metadata = new Dictionary<string, string>() },
            "fee" => intent with { ApplicationFeeMinor = 1 },
            "missing" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Respond(paid with { NativeIntentEvidence = invalid });
        await using var context = Database.CreateContext();
        var result = await Build(context).RunAsync(10, CancellationToken.None);
        result.Failures.Should().Be(1);
        (await context.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        var journal = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync();
        journal.ProviderSessionId.Should().Be(paid.Id);
        journal.LeaseId.Should().NotBeNull();
        _effects.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_known_session_and_expired_create_window_never_fall_back_to_create()
    {
        await PrimeAsync();
        Clock.Now = Clock.Now.AddDays(2);
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);
        await using var context = Database.CreateContext();
        var result = await Build(context).RunAsync(10, CancellationToken.None);
        result.Failures.Should().Be(1);
        (await context.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task CompleteEffectsAsync()
    {
        await using var write = Database.CreateContext();
        await write.Set<CustomerOrderCheckoutJournal>().ExecuteUpdateAsync(update =>
            update.SetProperty(value => value.EffectsCompletedAt, Clock.Now.UtcDateTime)
                .SetProperty(value => value.LeaseId, (Guid?)null).SetProperty(value => value.LeaseExpiresAt, (DateTime?)null));
    }

    [Fact]
    public async Task Poisoned_oldest_batch_backs_off_before_payload_validation_and_next_sweep_reaches_valid_capture()
    {
        var lease = await PrimeAsync();
        var poison = CustomerOrderCheckoutReplayTests.Journal();
        poison.OrderId = Guid.NewGuid();
        poison.OperationId = Guid.NewGuid();
        poison.CreateIdempotencyKey = $"sofra:native-order:v1:{poison.OperationId:D}";
        poison.ProviderSessionId = "cs_poison_fixture";
        poison.StartedAt = Clock.Now.AddDays(-3).UtcDateTime;
        poison.ExpiresAt = poison.StartedAt.AddHours(1);
        poison.MaximumCreateRetryAt = poison.StartedAt.AddHours(23);
        await using (var write = Database.CreateContext())
        {
            write.Set<CustomerOrderCheckoutJournal>().Add(poison);
            await write.SaveChangesAsync();
        }
        Respond(CustomerOrderCheckoutSettlementTests.Paid(lease));
        _effects.Setup(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>())).Returns(CompleteEffectsAsync);
        await using var context = Database.CreateContext();
        var worker = Build(context);
        var failed = await worker.RunAsync(1, CancellationToken.None);
        failed.Examined.Should().Be(1);
        failed.Failures.Should().Be(1);
        var valid = await worker.RunAsync(1, CancellationToken.None);
        valid.Settled.Should().Be(1);
        valid.Failures.Should().Be(0);
        _provider.Verify(value => value.GetAsync("cs_poison_fixture", It.IsAny<CancellationToken>()), Times.Never);
        (await context.Orders.AsNoTracking().SingleAsync()).TotalPaid.Should().Be(12.34m);
    }

    [Fact]
    public async Task Crash_after_money_commit_is_recovered_without_app_return_or_another_provider_read()
    {
        var lease = await PrimeAsync();
        Respond(CustomerOrderCheckoutSettlementTests.Paid(lease));
        _effects.Setup(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("injected post-commit process failure"));
        await using var context = Database.CreateContext();
        var worker = Build(context);
        (await worker.RunAsync(10, CancellationToken.None)).Failures.Should().Be(1);
        (await context.Orders.AsNoTracking().SingleAsync()).TotalPaid.Should().Be(12.34m);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().BeNull();
        _effects.Setup(value => value.CompleteAsync(lease.Request.OrderId, It.IsAny<CancellationToken>())).Returns(CompleteEffectsAsync);
        (await worker.RunAsync(10, CancellationToken.None)).Failures.Should().Be(0);
        (await worker.RunAsync(10, CancellationToken.None)).Examined.Should().Be(0);
        _provider.Verify(value => value.GetAsync(lease.ProviderSessionId ?? "cs_native_canonical_fixture", It.IsAny<CancellationToken>()), Times.Once);
        (await context.OrderPayments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Polling_budget_cancels_slow_provider_and_keeps_the_original_reference_for_a_later_attempt()
    {
        await PrimeAsync();
        Settings.ReconciliationBudgetSeconds = 1;
        var cancelled = false;
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return (StripeCheckoutSession?)null;
            });
        await using var context = Database.CreateContext();
        var result = await Build(context).RunAsync(10, CancellationToken.None);
        cancelled.Should().BeTrue();
        result.Failures.Should().Be(1);
        var journal = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync();
        journal.ProviderSessionId.Should().Be("cs_native_canonical_fixture");
        journal.LeaseId.Should().NotBeNull();
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Provider_failure_reports_only_safe_classification_and_never_exception_payloads()
    {
        await PrimeAsync();
        const string privateDetails = "private-provider-url-credential-fixture";
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(privateDetails));
        var log = new SafeLogger();
        await using var context = Database.CreateContext();
        (await Build(context, log).RunAsync(10, CancellationToken.None)).Failures.Should().Be(1);
        log.Message.Should().Contain(nameof(IOException)).And.NotContain(privateDetails);
        log.Error.Should().BeNull();
        (await context.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }

    private sealed class SafeLogger : ILogger<CustomerOrderCheckoutReconciler>
    {
        public string Message { get; private set; } = string.Empty;
        public Exception? Error { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Message = formatter(state, exception);
            Error = exception;
        }
    }
}
