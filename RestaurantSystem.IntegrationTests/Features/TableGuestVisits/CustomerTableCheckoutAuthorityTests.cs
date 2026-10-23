using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed partial class CustomerTableCheckoutTests
{
    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("hash")]
    [InlineData("ciphertext")]
    [InlineData("invalid-jwt")]
    public async Task Foreign_proof_cannot_start_recover_or_cancel_original_checkout(string field)
    {
        var proof = (await AcceptedAsync()).Proof;
        var changed = field switch
        {
            "tenant" => proof with { TenantId = Guid.NewGuid() },
            "operation" => proof with { OperationId = Guid.NewGuid() },
            "hash" => proof with { PayloadHash = new string('a', 64) },
            "ciphertext" => proof with { OwnerProof = "invalid" },
            _ => proof
        };
        if (field == "invalid-jwt") Client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-customer-token");
        foreach (var action in new[] { "start", "recover", "stop" })
        {
            var response = await SendAsync(CheckoutPath + action, changed, field == "session" ? Guid.NewGuid().ToString("D") : null);
            response.IsSuccessStatusCode.Should().BeFalse();
            if (field == "session") response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        Creates.Should().BeEmpty();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Reserved);
    }

    [Fact]
    public async Task Concurrent_starts_keep_one_journal_and_one_provider_creation()
    {
        var proof = (await AcceptedAsync()).Proof;
        var results = await Task.WhenAll(CheckoutAsync("start", proof), CheckoutAsync("start", proof));
        results[0].AttemptId.Should().Be(results[1].AttemptId);
        Creates.Should().ContainSingle();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        ProviderInsideTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_during_escaped_creation_retains_unknown_until_canonical_nonpayment()
    {
        var proof = (await AcceptedAsync()).Proof;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeforeCreate = _ => entered.TrySetResult(); CreateWait = release.Task;
        var starting = CheckoutAsync("start", proof);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopping = await CheckoutAsync("stop", proof);
            stopping.Outcome.Should().Be("pending"); stopping.CheckoutUrl.Should().BeNull();
            await using var context = DatabaseFixture.CreateContext();
            (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.CancelRequested);
            (await context.AccountCheckoutJournals.SingleAsync()).CancelRequestedAt.Should().NotBeNull();
            (await context.OrderPayments.CountAsync()).Should().Be(0);
        }
        finally { release.TrySetResult(); }
        (await starting).CheckoutUrl.Should().BeNull();
        (await CheckoutAsync("recover", proof)).Outcome.Should().Be("stopped");
        Creates.Should().ContainSingle(); ProviderInsideTransaction.Should().BeFalse();
    }
    [Fact]
    public async Task Erasure_after_provider_read_hides_private_result_but_retains_and_posts_original_money()
    {
        var customer = await SignInCustomerAsync();
        var proof = (await AcceptedAsync()).Proof;
        await CheckoutAsync("start", proof); Captured();
        AfterGet = () =>
        {
            using var scope = Factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<CustomMediator>()
                .SendCommand(new DeleteUserCommand(customer, true)).GetAwaiter().GetResult().Success.Should().BeTrue();
        };
        var response = await SendAsync(CheckoutPath + "recover", proof);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AfterGet = null;
        await using var context = DatabaseFixture.CreateContext();
        (await context.Users.IgnoreQueryFilters().AnyAsync(row => row.Id == customer)).Should().BeFalse();
        (await context.AccountCheckoutJournals.SingleAsync()).NativeOperationId.Should().Be(proof.OperationId);
        (await context.Set<CustomerTablePaymentOperation>().SingleAsync()).AttemptId.Should().NotBeNull();
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
        (await context.OrderPayments.SingleAsync()).Amount.Should().Be(14m);
        Creates.Should().ContainSingle(); ProviderInsideTransaction.Should().BeFalse();
    }
}
