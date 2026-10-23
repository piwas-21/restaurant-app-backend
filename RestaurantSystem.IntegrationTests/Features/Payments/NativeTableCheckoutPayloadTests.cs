using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class NativeTableCheckoutPayloadTests
{
    private static readonly Guid Attempt = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Operation = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime Started = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private static AccountStripeCheckoutRequest Request() => new()
    {
        AttemptId = Attempt,
        Context = new("acct_legacy", false),
        AmountMinor = 1400,
        Currency = "CHF",
        ExpiresAt = Started.AddMinutes(30),
        IdempotencyKey = $"sofra:account-payment:v1:{Attempt:D}",
        ReturnBaseUrl = "https://restaurant.example.test/checkout/account-return"
    };

    [Fact]
    public void Ordinary_frozen_requests_preserve_the_pre_extension_wire_bytes_and_hash()
    {
        // Literal oracle was specified independently of the serializer/hash under test.
        const string originalJson = """
            {"AttemptId":"11111111-1111-4111-8111-111111111111","Context":{"ConnectedAccountId":"acct_legacy","LiveMode":false},"AmountMinor":1400,"Currency":"CHF","ExpiresAt":"2026-10-23T12:30:00Z","IdempotencyKey":"sofra:account-payment:v1:11111111-1111-4111-8111-111111111111","ReturnBaseUrl":"https://restaurant.example.test/checkout/account-return"}
            """;
        JsonSerializer.Serialize(Request()).Should().Be(originalJson.Trim());
        AccountCheckoutReplayPayload.Hash(Request()).Should().Be("F342924DA65B2D167D449AE07D4492EBF8D5531314264FBFE17CC86A03101D2E"); // pragma: allowlist secret -- independent historical payload digest, not a credential
        var journal = Journal(Request());
        AccountCheckoutReplayPayload.Read(journal, Started).NativeScope.Should().BeNull();
        AccountCheckoutReplayPayload.Read(journal, Started.AddHours(1)).Should().BeEquivalentTo(Request());
    }

    [Fact]
    public void Native_identity_is_frozen_and_expiry_cannot_create_a_replacement()
    {
        var native = new NativeAccountCheckoutScope(Tenant, Operation);
        var request = Request() with
        {
            NativeScope = native,
            ReturnBaseUrl = native.ReturnBase("https://links.example.test", "fixture", Attempt)
        };
        var journal = Journal(request);
        var replay = AccountCheckoutReplayPayload.Read(journal, Started.AddMinutes(1));
        replay.Should().BeEquivalentTo(request);
        native.ReturnUrl(replay.ReturnBaseUrl, Attempt).Should().Be(
            $"https://links.example.test/r/fixture/table-payment-return/{Attempt:D}?operation={Operation:D}");
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Read(journal, request.ExpiresAt)).Should().Throw<ConflictException>();
        journal.ProviderSessionId = "cs_original";
        AccountCheckoutReplayPayload.Frozen(journal).Should().BeEquivalentTo(request);
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Read(journal, Started)).Should().Throw<ConflictException>();
        journal.NativeOperationId = Guid.NewGuid();
        FluentActions.Invoking(() => AccountCheckoutReplayPayload.Frozen(journal)).Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("/r/fixture/table-payment-return/")]
    [InlineData("https://links.example.test/r/fixture/payment-return/")]
    [InlineData("http://links.example.test/r/fixture/table-payment-return/")]
    [InlineData("https://links.example.test:444/r/fixture/table-payment-return/")]
    [InlineData("https://user@links.example.test/r/fixture/table-payment-return/")]
    public void Unsafe_or_order_return_paths_cannot_authorize_native_checkout(string prefix)
    {
        FluentActions.Invoking(() => new NativeAccountCheckoutScope(Tenant, Operation)
            .RequireReturnBase(prefix + Attempt.ToString("D"), Attempt)).Should().Throw<BadRequestException>();
    }

    private static AccountCheckoutJournal Journal(AccountStripeCheckoutRequest request) => new()
    {
        CreatedBy = "payload-fixture",
        AttemptId = request.AttemptId,
        StartedAttemptVersion = 1,
        StartedAt = Started,
        AmountMinor = request.AmountMinor,
        Currency = request.Currency,
        ExpiresAt = request.ExpiresAt,
        ProviderAccountId = request.Context.ConnectedAccountId,
        ProviderLiveMode = request.Context.LiveMode,
        CreateIdempotencyKey = request.IdempotencyKey,
        CreatePayloadHash = AccountCheckoutReplayPayload.Hash(request),
        ReturnBaseUrl = request.ReturnBaseUrl,
        MaximumCreateRetryAt = Started.AddHours(12),
        NativeOperationId = request.NativeScope?.OperationId,
        NativeTenantId = request.NativeScope?.TenantId
    };
}
