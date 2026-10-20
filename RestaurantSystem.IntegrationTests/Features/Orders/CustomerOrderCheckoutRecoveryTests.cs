using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutRecoveryTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    private readonly Mock<IStripeCheckoutClient> _provider = new(MockBehavior.Strict);
    private readonly Mock<IStripeGateway> _gateway = new(MockBehavior.Strict);
    private readonly StripeSettings _stripe = new() { PlatformApiKey = "rk_test_native_fixture" }; // pragma: allowlist secret

    private Task<StripeCheckoutSession> RecoverAsync(Guid? owner = null) => WithCheckoutAsync(async (context, _, leases) =>
    {
        context.Database.CurrentTransaction.Should().BeNull();
        var recovery = new CustomerOrderCheckoutRecovery(leases, _provider.Object, _gateway.Object, Options.Create(_stripe), Clock, Mock.Of<ICustomerOrderCheckoutEffects>(),
            new CustomerOrderCheckoutProviderReader(_provider.Object, _gateway.Object, Options.Create(_stripe)));
        return await recovery.RecoverAsync(Session, Proof, CancellationToken.None);
    }, owner);

    private void ConfigureGateway(string account = "acct_native_fixture")
    {
        _gateway.SetupGet(value => value.IsConfigured).Returns(true);
        _gateway.SetupGet(value => value.ConnectedAccountId).Returns(account);
    }

    [Fact]
    public async Task Lost_create_response_replays_exact_payload_without_holding_an_order_transaction_then_reads_canonical_state()
    {
        var orderId = await SeedAsync();
        ConfigureGateway();
        var requests = new List<CheckoutSessionRequest>();
        StripeCheckoutSession? remote = null;
        _provider.Setup(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (CheckoutSessionRequest request, CancellationToken token) =>
            {
                // A different context can take the order lock during provider I/O. This would time
                // out if the caller carried its lease transaction across the network boundary.
                await using var independent = Database.CreateContext();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await using var unlocked = await OrderAccountMutationScope.BeginAsync(independent, orderId, timeout.Token);
                requests.Add(request);
                remote = CustomerOrderCheckoutLeaseTests.Evidence(request);
                if (requests.Count == 1) throw new HttpRequestException("Lost provider response fixture");
                return remote;
            });
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => remote! with { Status = "complete", PaymentStatus = "unpaid", Url = null });
        await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<HttpRequestException>();
        await using (var read = Database.CreateContext())
        {
            var journal = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
            journal.ProviderSessionId.Should().BeNull();
            journal.LeaseId.Should().NotBeNull();
        }
        await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<ConflictException>();
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Clock.Now = Clock.Now.AddMinutes(1);
        var recovered = await RecoverAsync();
        recovered.Status.Should().Be("complete");
        recovered.PaymentStatus.Should().Be("unpaid");
        recovered.Url.Should().BeNull();
        requests.Should().HaveCount(2);
        requests[1].Should().BeEquivalentTo(requests[0]);
        await using var stored = Database.CreateContext();
        var final = await stored.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        final.ProviderSessionId.Should().Be("cs_native_canonical_fixture");
        final.LeaseId.Should().BeNull();
        final.LastVerifiedAt.Should().Be(Clock.Now.UtcDateTime);
        (await stored.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        (await stored.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        _provider.Verify(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("money")]
    public async Task Canonical_reread_must_exist_and_match_the_created_session_before_any_evidence_is_saved(string scenario)
    {
        await SeedAsync();
        ConfigureGateway();
        StripeCheckoutSession? created = null;
        _provider.Setup(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSessionRequest request, CancellationToken _) =>
                created = CustomerOrderCheckoutLeaseTests.Evidence(request));
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => scenario switch
            {
                "valid" => created,
                "missing" => null,
                "different" => created! with { Id = "cs_wrong_fixture" },
                "money" => created! with { AmountTotalMinor = 1 },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            });
        if (scenario == "valid")
            (await RecoverAsync()).Url.Should().Be("https://checkout.stripe.com/fixture");
        else
            await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<ConflictException>();
        await using var read = Database.CreateContext();
        var journal = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        if (scenario == "valid")
        {
            journal.ProviderSessionId.Should().Be("cs_native_canonical_fixture");
            journal.LastVerifiedAt.Should().Be(Clock.Now.UtcDateTime);
            journal.LeaseId.Should().BeNull();
        }
        else
        {
            journal.ProviderSessionId.Should().BeNull();
            journal.LastVerifiedAt.Should().BeNull();
            journal.LeaseId.Should().NotBeNull();
        }
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Missing_known_session_never_falls_back_to_create_even_after_retry_window_and_settings_withdrawal()
    {
        await SeedAsync();
        ConfigureGateway();
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutLeaseTests.Evidence(lease), CancellationToken.None);
            return true;
        });
        Settings.Enabled = false;
        Clock.Now = Clock.Now.AddDays(2);
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);
        await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<ConflictException>();
        Clock.Now = Clock.Now.AddMinutes(1);
        await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<ConflictException>();
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().SingleAsync()).ProviderSessionId.Should().Be("cs_native_canonical_fixture");
    }

    [Theory]
    [InlineData("account")]
    [InlineData("mode")]
    public async Task Current_provider_context_drift_refuses_network_io(string scenario)
    {
        await SeedAsync();
        ConfigureGateway(scenario == "account" ? "acct_other" : "acct_native_fixture");
        if (scenario == "mode") _stripe.PlatformApiKey = "rk_live_native_fixture"; // pragma: allowlist secret
        await FluentActions.Invoking(() => RecoverAsync()).Should().ThrowAsync<ConflictException>();
        _provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("closed", false)]
    [InlineData("withdrawn", false)]
    [InlineData("module", false)]
    [InlineData("partial", false)]
    [InlineData("closed", true)]
    [InlineData("withdrawn", true)]
    [InlineData("module", true)]
    [InlineData("partial", true)]
    [InlineData("expired", true)]
    public async Task Changed_eligibility_retains_evidence_but_withholds_the_payable_url(string scenario, bool known)
    {
        await SeedAsync();
        ConfigureGateway();
        StripeCheckoutSession? created = null;
        if (known)
        {
            await WithCheckoutAsync(async (_, _, leases) =>
            {
                var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
                created = CustomerOrderCheckoutLeaseTests.Evidence(lease);
                await leases.RecordAsync(Session, Proof, lease, created, CancellationToken.None);
                return true;
            });
            if (scenario == "expired") Clock.Now = Clock.Now.AddMinutes(30).AddSeconds(30);
            else await ChangeStateAsync();
        }
        _provider.Setup(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSessionRequest request, CancellationToken _) =>
                created = CustomerOrderCheckoutLeaseTests.Evidence(request));
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (scenario == "expired") Clock.Now = Clock.Now.AddSeconds(31);
                else if (!known) await ChangeStateAsync();
                return created;
            });
        (await RecoverAsync()).Url.Should().BeNull();
        await using var read = Database.CreateContext();
        var journal = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        journal.ProviderSessionId.Should().Be("cs_native_canonical_fixture");
        journal.LastVerifiedAt.Should().Be(Clock.Now.UtcDateTime);
        journal.LeaseId.Should().BeNull();
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        if (known) _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        async Task ChangeStateAsync()
        {
            await using var write = Database.CreateContext();
            switch (scenario)
            {
                case "closed": (await write.Orders.SingleAsync()).Status = OrderStatus.Cancelled; break;
                case "withdrawn": Settings.Enabled = false; break;
                case "module": OnlineCapability = "disabled"; break;
                case "partial": (await write.Orders.SingleAsync()).TotalPaid = 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await write.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_worker_paused_after_acquisition_rechecks_deadlines_before_provider_io(bool pastReplayDeadline)
    {
        await SeedAsync();
        ConfigureGateway();
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var paused = new Mock<ICustomerOrderCheckoutLeaseStore>(MockBehavior.Strict);
            paused.Setup(value => value.AcquireAsync(Session, Proof, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
                    Clock.Now = pastReplayDeadline ? Clock.Now.AddDays(1) : Clock.Now.AddMinutes(1);
                    return lease;
                });
            var recovery = new CustomerOrderCheckoutRecovery(paused.Object, _provider.Object, _gateway.Object,
                Options.Create(_stripe), Clock, Mock.Of<ICustomerOrderCheckoutEffects>(),
            new CustomerOrderCheckoutProviderReader(_provider.Object, _gateway.Object, Options.Create(_stripe)));
            await FluentActions.Invoking(() => recovery.RecoverAsync(Session, Proof, CancellationToken.None))
                .Should().ThrowAsync<ConflictException>();
            _provider.VerifyNoOtherCalls();
            return true;
        });
    }

    [Fact]
    public async Task Owner_erasure_during_provider_io_withdraws_the_result_without_releasing_or_replacing_the_original_request()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner);
        ConfigureGateway();
        StripeCheckoutSession? created = null;
        _provider.Setup(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSessionRequest request, CancellationToken _) =>
                created = CustomerOrderCheckoutLeaseTests.Evidence(request));
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var write = Database.CreateContext();
                (await write.Orders.SingleAsync()).UserId = null;
                (await write.Users.SingleAsync(value => value.Id == owner)).IsDeleted = true;
                await write.SaveChangesAsync();
                return created;
            });
        await FluentActions.Invoking(() => RecoverAsync(owner)).Should().ThrowAsync<NotFoundException>();
        await using var read = Database.CreateContext();
        var journal = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        journal.ProviderSessionId.Should().BeNull();
        journal.LeaseId.Should().NotBeNull();
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }
}
