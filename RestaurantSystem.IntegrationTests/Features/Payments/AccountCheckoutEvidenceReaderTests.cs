using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutEvidenceReaderTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);
    private static readonly AccountStripeContext ProviderContext = new("acct_reader", false);
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly Mock<IAccountStripeCheckoutClient> _provider = new(MockBehavior.Strict);

    [Fact]
    public async Task A_known_session_is_read_without_creating_a_new_checkout()
    {
        var journal = Journal();
        journal.ProviderSessionId = "cs_reader";
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        _provider.Setup(value => value.GetAsync("cs_reader", It.IsAny<CancellationToken>())).ReturnsAsync(Session());
        var evidence = await Reader().ReadAsync(journal, true, CancellationToken.None);
        evidence.Session.Id.Should().Be("cs_reader");
        evidence.CanRelease(journal).Should().BeFalse();
        _provider.Verify(value => value.CreateAsync(It.IsAny<AccountStripeCheckoutRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_create_response_replays_only_the_exact_original_payload()
    {
        var journal = Journal();
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        _provider.Setup(value => value.CreateAsync(It.IsAny<AccountStripeCheckoutRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Session());
        var evidence = await Reader().ReadAsync(journal, true, CancellationToken.None);
        evidence.Session.AmountMinor.Should().Be(334);
        _provider.Verify(value => value.CreateAsync(It.Is<AccountStripeCheckoutRequest>(request =>
            request.IdempotencyKey == journal.CreateIdempotencyKey && request.ExpiresAt == journal.ExpiresAt
            && request.AmountMinor == 334 && request.Context == ProviderContext
            && request.ReturnBaseUrl == journal.ReturnBaseUrl), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Missing_known_provider_session_holds_the_original_contribution()
    {
        var journal = Journal(); journal.ProviderSessionId = "cs_reader";
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        _provider.Setup(value => value.GetAsync("cs_reader", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountStripeSession?)null);
        var read = () => Reader().ReadAsync(journal, true, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
        _provider.Verify(value => value.CreateAsync(It.IsAny<AccountStripeCheckoutRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Provider_account_drift_never_reads_or_creates_in_the_new_account()
    {
        _provider.Setup(value => value.ReadContext()).Returns(new AccountStripeContext("acct_changed", false));
        var read = () => Reader().ReadAsync(Journal(), true, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
        _provider.Verify(value => value.ReadContext(), Times.Once);
        _provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Retry_cutoff_never_mints_a_new_provider_request()
    {
        var journal = Journal(); journal.MaximumCreateRetryAt = Now;
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        var read = () => Reader().ReadAsync(journal, true, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
        _provider.Verify(value => value.CreateAsync(It.IsAny<AccountStripeCheckoutRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancellation_releases_only_after_reading_expired_unpaid_provider_evidence()
    {
        var journal = Journal(); journal.ProviderSessionId = "cs_reader"; journal.CancelRequestedAt = Now;
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        _provider.SetupSequence(value => value.GetAsync("cs_reader", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session()).ReturnsAsync(Session() with { Status = "expired" });
        _provider.Setup(value => value.ExpireAsync("cs_reader", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var evidence = await Reader().ReadAsync(journal, true, CancellationToken.None);
        evidence.CanRelease(journal).Should().BeTrue();
        _provider.Verify(value => value.GetAsync("cs_reader", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Missing_intent_evidence_never_proves_non_payment()
    {
        var journal = Journal(); journal.ProviderSessionId = "cs_reader";
        _provider.Setup(value => value.ReadContext()).Returns(ProviderContext);
        _provider.Setup(value => value.GetAsync("cs_reader", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session() with { IntentId = "pi_reader", Status = "expired" });
        _provider.Setup(value => value.GetIntentAsync("pi_reader", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountStripeIntent?)null);
        var read = () => Reader().ReadAsync(journal, true, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
    }

    [Theory]
    [InlineData("https://checkout.stripe.com/c/pay/cs_safe", true)]
    [InlineData("https://checkout.stripe.com.attacker.test/c/pay/cs_safe", false)]
    [InlineData("http://checkout.stripe.com/c/pay/cs_safe", false)]
    [InlineData("https://checkout.stripe.com:444/c/pay/cs_safe", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Checkout_redirect_is_limited_to_the_canonical_host(string url, bool allowed)
    {
        (AccountCheckoutStatusReader.SafeCheckoutUrl(url) is not null).Should().Be(allowed);
    }

    private AccountCheckoutEvidenceReader Reader() => new(_provider.Object, new FixedClock());

    private AccountCheckoutJournal Journal()
    {
        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = _attemptId,
            Context = ProviderContext,
            AmountMinor = 334,
            Currency = "CHF",
            ExpiresAt = Now.AddHours(1),
            IdempotencyKey = AccountCheckoutReplayPayload.CreateKey(_attemptId),
            ReturnBaseUrl = "https://tenant.test/table-account"
        };
        return new AccountCheckoutJournal
        {
            AttemptId = _attemptId,
            StartedAttemptVersion = 2,
            AmountMinor = 334,
            Currency = "CHF",
            CreatedBy = "opaque-guest",
            ProviderAccountId = ProviderContext.ConnectedAccountId,
            StartedAt = Now.AddMinutes(-1),
            ExpiresAt = request.ExpiresAt,
            MaximumCreateRetryAt = Now.AddHours(12),
            CreateIdempotencyKey = request.IdempotencyKey,
            CreatePayloadHash = AccountCheckoutReplayPayload.Hash(request),
            ReturnBaseUrl = request.ReturnBaseUrl
        };
    }

    private AccountStripeSession Session() => new()
    {
        Id = "cs_reader",
        Context = ProviderContext,
        AmountMinor = 334,
        Currency = "chf",
        Status = "open",
        PaymentStatus = "unpaid",
        ClientReferenceId = _attemptId.ToString("D"),
        Metadata = new Dictionary<string, string>
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = _attemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
        }
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
