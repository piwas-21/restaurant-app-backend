using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed partial class CustomerTableCheckoutTests(DatabaseFixture fixture) : CustomerTableCheckoutFixture(fixture)
{
    [Fact]
    public async Task Frozen_request_commits_before_io_and_lost_create_replays_only_the_original()
    {
        var prepared = await AcceptedAsync(); LoseFirstCreate = true;
        BeforeCreate = request =>
        {
            using var context = DatabaseFixture.CreateContext();
            var saved = context.AccountCheckoutJournals.Single();
            saved.AttemptId.Should().Be(request.AttemptId); saved.NativeOperationId.Should().Be(prepared.Proof.OperationId);
            saved.NativeTenantId.Should().Be(NativeTenantId); saved.AmountMinor.Should().Be(1400);
            AccountCheckoutReplayPayload.Frozen(saved).Should().BeEquivalentTo(request);
            context.AccountPaymentAttempts.Single().State.Should().Be(AccountPaymentState.Starting);
            context.OrderPayments.Count().Should().Be(0);
        };
        var first = await CheckoutAsync("start", prepared.Proof);
        first.Outcome.Should().Be("pending"); first.CheckoutUrl.Should().BeNull();
        BeforeCreate = null;
        var retry = await CheckoutAsync("recover", prepared.Proof);
        retry.Outcome.Should().Be("ready"); retry.AttemptId.Should().Be(first.AttemptId);
        retry.AmountMinor.Should().Be(1400); retry.Currency.Should().Be("CHF");
        retry.CheckoutUrl.Should().Be("https://checkout.stripe.com/c/pay/native-table-fixture");
        Creates.Should().HaveCount(2); Creates[1].Should().BeEquivalentTo(Creates[0]);
        (await CheckoutAsync("start", prepared.Proof)).AttemptId.Should().Be(first.AttemptId);
        Creates.Should().HaveCount(2); Gets.Should().Be(1); ProviderInsideTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Known_session_loss_never_creates_another_session_or_releases_the_reservation()
    {
        var proof = (await AcceptedAsync()).Proof;
        var started = await CheckoutAsync("start", proof); started.Outcome.Should().Be("ready");
        Remote = null;
        var recovered = await CheckoutAsync("recover", proof);
        recovered.Outcome.Should().Be("pending"); recovered.CheckoutUrl.Should().BeNull();
        Creates.Should().ContainSingle();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.SingleAsync()).State.HoldsReservation().Should().BeTrue();
        (await context.AccountCheckoutJournals.SingleAsync()).ProviderSessionId.Should().Be("cs_native_table");
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Stop_before_freeze_survives_expiry_and_withdrawal_and_seals_late_start()
    {
        var proof = (await AcceptedAsync()).Proof;
        await using var context = DatabaseFixture.CreateContext();
        await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        (await CheckoutAsync("stop", proof)).Outcome.Should().Be("stopped");
        (await CheckoutAsync("start", proof)).Outcome.Should().Be("stopped");
        (await CheckoutAsync("recover", proof)).Outcome.Should().Be("stopped");
        Creates.Should().BeEmpty();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Released);
    }

    [Theory]
    [InlineData("missing-participant")]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    [InlineData("unsafe-host")]
    [InlineData("limits")]
    public async Task Original_recovery_withdraws_browser_offer_without_replacing_the_contribution(string scenario)
    {
        var proof = (await AcceptedAsync()).Proof;
        (await CheckoutAsync("start", proof)).Outcome.Should().Be("ready");
        await using var context = DatabaseFixture.CreateContext();
        if (scenario == "missing-participant") Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        if (scenario == "expired") await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        if (scenario == "withdrawn") WithdrawNative();
        if (scenario == "limits") Factory.Services.GetRequiredService<IOptions<AccountOnlineContributionSettings>>().Value.MinimumAmountMinor = 1500;
        if (scenario == "unsafe-host") Remote = Remote! with { Url = "https://unapproved.example.test/pay" };
        var recovered = await CheckoutAsync("recover", proof);
        recovered.Outcome.Should().Be("pending"); recovered.CheckoutUrl.Should().BeNull();
        Creates.Should().ContainSingle(); (await context.AccountCheckoutJournals.CountAsync()).Should().Be(1);
        (await context.AccountPaymentAttempts.SingleAsync()).State.HoldsReservation().Should().BeTrue();
    }

    [Fact]
    public async Task Canonical_cancellation_after_withdrawal_releases_only_the_original_unpaid_session()
    {
        var proof = (await AcceptedAsync()).Proof;
        await CheckoutAsync("start", proof); WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var stopped = await CheckoutAsync("stop", proof);
        stopped.Outcome.Should().Be("stopped"); stopped.CheckoutUrl.Should().BeNull();
        Creates.Should().ContainSingle(); ProviderInsideTransaction.Should().BeFalse();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Released);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await CheckoutAsync("start", proof)).Outcome.Should().Be("stopped");
    }

    [Fact]
    public async Task Canonical_capture_posts_exactly_one_tender_and_recovery_stays_private_after_close()
    {
        var proof = (await AcceptedAsync()).Proof;
        var started = await CheckoutAsync("start", proof); Captured();
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var paid = await CheckoutAsync("recover", proof);
        paid.Outcome.Should().Be("paid"); paid.CheckoutUrl.Should().BeNull(); paid.AttemptId.Should().Be(started.AttemptId);
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.SingleAsync()).Amount.Should().Be(14m);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
        await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TableServiceSessionStatus.Closed));
        (await CheckoutAsync("recover", proof)).Outcome.Should().Be("paid");
        (await CheckoutAsync("stop", proof)).Outcome.Should().Be("paid");
        (await context.OrderPayments.CountAsync()).Should().Be(1); Creates.Should().ContainSingle();
        ProviderInsideTransaction.Should().BeFalse();
        var fields = typeof(CustomerTableCheckoutDto).GetProperties().Select(row => row.Name);
        fields.Should().NotContain(new[] { "OrderId", "Orders", "Allocations", "ProviderAccountId", "OwnerProof", "ParticipantToken" });
        (await SendAsync(CheckoutPath + "recover", proof, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("open", "unpaid")]
    [InlineData("complete", "unpaid")]
    public async Task Contradictory_capture_retains_money_evidence_and_never_claims_paid_or_releases(string status, string payment)
    {
        var proof = (await AcceptedAsync()).Proof;
        await CheckoutAsync("start", proof); Captured(status, payment);
        (await CheckoutAsync("recover", proof)).Outcome.Should().Be("pending");
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        var journal = await context.AccountCheckoutJournals.SingleAsync();
        journal.ProviderCapturedMinor.Should().Be(1400); journal.ReconciliationRequired.Should().BeTrue();
        (await context.AccountPaymentAttempts.SingleAsync()).State.HoldsReservation().Should().BeTrue();
        Creates.Should().ContainSingle();
    }
    [Fact]
    public async Task Capture_winning_cancellation_race_posts_once_and_does_not_release()
    {
        var proof = (await AcceptedAsync()).Proof;
        await CheckoutAsync("start", proof); BeforeExpire = () => Captured();
        (await CheckoutAsync("stop", proof)).Outcome.Should().Be("paid");
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.SingleAsync()).Amount.Should().Be(14m);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
        (await context.AccountCheckoutJournals.SingleAsync()).CancelRequestedAt.Should().NotBeNull();
        (await CheckoutAsync("stop", proof)).Outcome.Should().Be("paid");
        (await context.OrderPayments.CountAsync()).Should().Be(1); Creates.Should().ContainSingle();
    }
}
