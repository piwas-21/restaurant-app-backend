using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentReadinessTests(DatabaseFixture fixture) : CustomerTableCheckoutFixture(fixture)
{
    private CustomerHostedCheckoutSettings Hosted
    {
        get
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IOptions<CustomerHostedCheckoutSettings>>().Value;
        }
    }

    [Fact]
    public async Task Public_bootstrap_exposes_only_gated_readiness_and_never_provider_authority()
    {
        var (text, capability) = await BootstrapAsync();
        capability.Should().Be("enabled");
        text.Should().NotContain("HostedPageOrigins").And.NotContain("TablePaymentsEnabled").And.NotContain(Account);
        Creates.Should().BeEmpty();
        Gateway.Invocations.Should().OnlyContain(call => call.Method.Name == "get_IsConfigured");
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("provider")]
    [InlineData("flag")]
    public async Task Withdrawn_readiness_refuses_new_preparation_without_financial_effects(string mode)
    {
        var intent = await PaymentIntentAsync();
        if (mode == "approval") Hosted.TablePaymentsEnabled = false;
        if (mode == "provider") Gateway.SetupGet(value => value.IsConfigured).Returns(false);
        if (mode == "flag") Features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(false);
        (await BootstrapAsync()).Capability.Should().Be(mode == "flag" ? "disabled" : "unknown");
        (await SendAsync(PreparationPath, intent)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NoPaymentEffectsAsync();
    }

    [Fact]
    public async Task Withdrawal_after_preparation_refuses_submission_but_original_stop_remains_permanent()
    {
        var intent = await PaymentIntentAsync();
        var prepared = await PreparedPaymentAsync(intent);
        var submission = new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof };
        Hosted.TablePaymentsEnabled = false;
        (await SendAsync("/api/customer-mobile/table-payments/submit", submission)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PaymentResolutionAsync(await SendAsync("/api/customer-mobile/table-payments/stop", prepared.Proof)))
            .Outcome.Should().Be(CustomerTablePaymentOutcome.Stopped);
        Hosted.TablePaymentsEnabled = true;
        (await PaymentResolutionAsync(await SendAsync("/api/customer-mobile/table-payments/submit", submission)))
            .Outcome.Should().Be(CustomerTablePaymentOutcome.Stopped);
        await NoPaymentEffectsAsync();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Original_reserved_recovery_and_stopping_survive_approval_withdrawal_without_starting_provider()
    {
        var prepared = await AcceptedAsync();
        Hosted.TablePaymentsEnabled = false;
        (await SendAsync(CheckoutPath + "start", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var recovered = await CheckoutAsync("recover", prepared.Proof);
        recovered.Outcome.Should().Be("reserved");
        recovered.CheckoutUrl.Should().BeNull();
        (await CheckoutAsync("stop", prepared.Proof)).Outcome.Should().Be("stopped");
        Creates.Should().BeEmpty();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Released);
    }

    [Fact]
    public async Task Original_capture_remains_canonical_after_approval_withdrawal_without_returning_a_browser_URL()
    {
        var prepared = await AcceptedAsync();
        (await CheckoutAsync("start", prepared.Proof)).Outcome.Should().Be("ready");
        Hosted.TablePaymentsEnabled = false;
        var pending = await CheckoutAsync("recover", prepared.Proof);
        pending.Outcome.Should().Be("pending");
        pending.CheckoutUrl.Should().BeNull();
        Captured();
        var paid = await CheckoutAsync("recover", prepared.Proof);
        paid.Outcome.Should().Be("paid");
        paid.CheckoutUrl.Should().BeNull();
        Creates.Should().ContainSingle();
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.SumAsync(value => value.Amount)).Should().Be(14m);
    }
    private async Task<(string Text, string Capability)> BootstrapAsync()
    {
        using var response = await Client.GetAsync(new Uri("/api/tenant/mobile/bootstrap?contractVersion=1", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("capabilities").GetProperty("table-visits").GetString().Should().Be("enabled");
        return (text, data.GetProperty("capabilities").GetProperty("table-payments").GetString()!);
    }

}
