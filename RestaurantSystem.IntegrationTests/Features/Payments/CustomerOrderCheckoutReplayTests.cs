using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class CustomerOrderCheckoutReplayTests : NativeCheckoutProviderFixture
{
    private static readonly DateTime Started = Expiry.AddMinutes(-31);
    private const string ExpectedPayloadHash = "B1919A008068E33A12FB9D8C194262172C727A48F448C8164E3D6CC28F68832C"; // pragma: allowlist secret -- independent non-secret request SHA256 fixture

    internal static CustomerOrderCheckoutJournal Journal() => new()
    {
        OrderId = OrderId,
        OperationId = OperationId,
        OrderNumber = "NATIVE-0001",
        Currency = "chf",
        AmountMinor = 1234,
        ProviderAccountId = Account,
        ReturnUrl = Return,
        CreateIdempotencyKey = $"sofra:native-order:v1:{OperationId:D}",
        CreatePayloadHash = ExpectedPayloadHash,
        StartedAt = Started,
        ExpiresAt = Expiry,
        MaximumCreateRetryAt = Started.AddHours(23)
    };

    [Fact]
    public void Original_request_matches_independent_wire_and_hash_fixture()
    {
        CustomerOrderCheckoutReplay.Hash(Request()).Should()
            .Be(ExpectedPayloadHash);
        var first = CustomerOrderCheckoutReplay.Read(Journal(), Started, forCreate: true);
        var retry = CustomerOrderCheckoutReplay.Read(Journal(), Started.AddHours(1), forCreate: true);
        first.Should().BeEquivalentTo(Request());
        retry.Should().BeEquivalentTo(first);
        retry.ExpiresAt.Should().Be(Expiry); // Never a fresh 31 minutes on recovery.
        retry.ApplicationFeeMinor.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(82799, true)]
    [InlineData(82800, false)]
    [InlineData(86400, false)]
    public void Create_window_is_strictly_bounded_before_provider_key_pruning(int seconds, bool allowed)
    {
        var read = () => CustomerOrderCheckoutReplay.Read(Journal(), Started.AddSeconds(seconds), forCreate: true);
        if (allowed) read.Should().NotThrow();
        else read.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Known_session_requires_canonical_read_and_never_repeats_create_even_inside_window()
    {
        var journal = Journal();
        journal.ProviderSessionId = "cs_native_fixture";
        FluentActions.Invoking(() => CustomerOrderCheckoutReplay.Read(journal, Started, forCreate: true))
            .Should().Throw<ConflictException>();
        CustomerOrderCheckoutReplay.Read(journal, Started.AddDays(2), forCreate: false)
            .Should().BeEquivalentTo(Request());
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Replay_deadline_requires_an_explicit_UTC_clock(DateTimeKind kind)
    {
        FluentActions.Invoking(() => CustomerOrderCheckoutReplay.Read(Journal(), DateTime.SpecifyKind(Started, kind), forCreate: true))
            .Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("order")]
    [InlineData("operation")]
    [InlineData("number")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("fee-zero")]
    [InlineData("fee-added")]
    [InlineData("account")]
    [InlineData("mode")]
    [InlineData("key")]
    [InlineData("return")]
    [InlineData("hash")]
    [InlineData("start")]
    [InlineData("fractional-start")]
    [InlineData("expiry")]
    [InlineData("retry-extension")]
    public void Frozen_payload_tampering_is_refused_before_any_provider_retry(string scenario)
    {
        CustomerOrderCheckoutReplay.Read(Journal(), Started, forCreate: true).Should().NotBeNull();
        var journal = Journal();
        switch (scenario)
        {
            case "order": journal.OrderId = Guid.NewGuid(); break;
            case "operation": journal.OperationId = Guid.NewGuid(); break;
            case "number": journal.OrderNumber = "DIFFERENT"; break;
            case "currency": journal.Currency = "eur"; break;
            case "amount": journal.AmountMinor++; break;
            case "fee-zero": journal.ApplicationFeeMinor = 0; break;
            case "fee-added": journal.ApplicationFeeMinor = 12; break;
            case "account": journal.ProviderAccountId = "acct_other"; break;
            case "mode": journal.ProviderLiveMode = true; break;
            case "key": journal.CreateIdempotencyKey += ":2"; break;
            case "return": journal.ReturnUrl = journal.ReturnUrl.Replace("links.example", "changed.example", StringComparison.Ordinal); break;
            case "hash": journal.CreatePayloadHash = new string('A', 64); break;
            case "start": journal.StartedAt = journal.StartedAt.AddSeconds(1); break;
            case "fractional-start": journal.StartedAt = journal.StartedAt.AddTicks(1); break;
            case "expiry": journal.ExpiresAt = journal.ExpiresAt.AddMinutes(1); break;
            case "retry-extension": journal.MaximumCreateRetryAt = journal.MaximumCreateRetryAt.AddHours(1); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        FluentActions.Invoking(() => CustomerOrderCheckoutReplay.Read(journal, Started, forCreate: true))
            .Should().Throw<ConflictException>();
    }
}
