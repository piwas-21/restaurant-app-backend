using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutReplayPayloadTests
{
    private static readonly DateTime StartedAt = new(2026, 10, 3, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lost_response_replay_keeps_every_original_provider_field()
    {
        var journal = Journal();
        var first = AccountCheckoutReplayPayload.Read(journal, StartedAt);
        var retry = AccountCheckoutReplayPayload.Read(journal, StartedAt.AddHours(11));
        retry.Should().BeEquivalentTo(first);
        retry.ExpiresAt.Should().Be(StartedAt.AddHours(1));
        retry.AmountMinor.Should().Be(334);
        retry.ReturnBaseUrl.Should().Be("https://tenant.test/table-account");
        retry.IdempotencyKey.Should().Be($"sofra:account-payment:v1:{journal.AttemptId:D}");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("return")]
    [InlineData("expiry")]
    [InlineData("key")]
    public void Mutated_provider_payload_is_rejected_instead_of_recreated(string field)
    {
        var journal = Journal();
        switch (field)
        {
            case "amount": journal.AmountMinor++; break;
            case "currency": journal.Currency = "EUR"; break;
            case "account": journal.ProviderAccountId = "acct_other"; break;
            case "environment": journal.ProviderLiveMode = true; break;
            case "return": journal.ReturnBaseUrl = "https://tenant.test/changed"; break;
            case "expiry": journal.ExpiresAt = journal.ExpiresAt.AddMinutes(1); break;
            case "key": journal.CreateIdempotencyKey = "fresh-key"; break;
        }
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Read(journal, StartedAt))
            .Should().Throw<ConflictException>();
    }

    [Fact]
    public void Provider_identity_or_retry_cutoff_requires_canonical_readback()
    {
        var journal = Journal();
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Read(journal, StartedAt.AddHours(12)))
            .Should().Throw<ConflictException>();
        journal.ProviderSessionId = "cs_known";
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Read(journal, StartedAt))
            .Should().Throw<ConflictException>();
    }

    private static AccountCheckoutJournal Journal()
    {
        var attemptId = Guid.NewGuid();
        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = attemptId,
            Context = new AccountStripeContext("acct_original", false),
            AmountMinor = 334,
            Currency = "CHF",
            ExpiresAt = StartedAt.AddHours(1),
            IdempotencyKey = AccountCheckoutReplayPayload.CreateKey(attemptId),
            ReturnBaseUrl = "https://tenant.test/table-account"
        };
        return new AccountCheckoutJournal
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            StartedAttemptVersion = 3,
            AmountMinor = 334,
            Currency = "CHF",
            ProviderAccountId = "acct_original",
            ProviderLiveMode = false,
            CreateIdempotencyKey = request.IdempotencyKey,
            CreatePayloadHash = AccountCheckoutReplayPayload.Hash(request),
            ReturnBaseUrl = request.ReturnBaseUrl,
            StartedAt = StartedAt,
            ExpiresAt = request.ExpiresAt,
            MaximumCreateRetryAt = StartedAt.AddHours(12),
            CreatedBy = "opaque-actor",
            CreatedAt = StartedAt
        };
    }
}
