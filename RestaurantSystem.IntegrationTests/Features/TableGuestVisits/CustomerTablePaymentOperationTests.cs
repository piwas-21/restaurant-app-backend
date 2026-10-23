using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentOperationTests(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    private const string Path = "/api/customer-mobile/table-payments/";

    [Theory]
    [InlineData(AccountPaymentMode.Full, 1400)]
    [InlineData(AccountPaymentMode.Amount, 350)]
    [InlineData(AccountPaymentMode.Items, 700)]
    [InlineData(AccountPaymentMode.Equal, 700)]
    [InlineData(AccountPaymentMode.CustomAmount, 400)]
    public async Task Accepts_exact_prepared_scope_and_one_permanent_reservation(AccountPaymentMode mode, long expected)
    {
        var intent = await PaymentIntentAsync(mode);
        await using var context = DatabaseFixture.CreateContext();
        if (mode == AccountPaymentMode.Amount) intent = intent with { AmountMinor = 350 };
        if (mode == AccountPaymentMode.Items)
        {
            var item = await context.OrderItems.SingleAsync();
            intent = intent with { SelectedUnits = [new(item.OrderId, item.Id, 2)] };
        }
        if (mode is AccountPaymentMode.Equal or AccountPaymentMode.CustomAmount)
        {
            using var scope = Factory.Services.CreateScope();
            var plan = await scope.ServiceProvider.GetRequiredService<IAccountEqualSharePlanService>().CreateGuestAsync(
                Visit.ServiceSessionId, Visit.ParticipantToken, new()
                {
                    OperationId = Guid.NewGuid(),
                    ExpectedAccountRevision = intent.ExpectedAccountRevision,
                    ShareCount = 2,
                    CustomAmountsMinor = mode == AccountPaymentMode.CustomAmount ? [1000, 400] : []
                }, CancellationToken.None);
            intent = mode == AccountPaymentMode.Equal ? intent with { EqualSharePlanId = plan.PlanId, EqualShareOrdinal = 2 }
                : intent with { CustomSharePlanId = plan.PlanId, CustomShareOrdinal = 2 };
        }
        var prepared = await PreparedPaymentAsync(intent);
        var first = await PaymentResolutionAsync(await SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto
        { Intent = intent, Proof = prepared.Proof }));
        first.Outcome.Should().Be(CustomerTablePaymentOutcome.Applied);
        first.State.Should().Be(AccountPaymentState.Reserved);
        first.OperationId.Should().Be(intent.OperationId);
        first.AttemptId.Should().NotBeNull();
        var attempt = await context.AccountPaymentAttempts.Include(row => row.Allocations).SingleAsync();
        attempt.Id.Should().Be(first.AttemptId.GetValueOrDefault()); attempt.OperationId.Should().NotBe(intent.OperationId);
        attempt.AmountMinor.Should().Be(expected); attempt.Currency.Should().Be("CHF");
        attempt.Allocations.Sum(row => row.AmountMinor).Should().Be(expected);
        attempt.Allocations.Select(row => new AccountPaymentAllocationDto(row.OrderId, row.OrderItemId,
            row.StartOrdinal, row.UnitCount, row.MinorPerUnit, row.AmountMinor)).Should().Equal(prepared.Allocations);
        var receipt = await context.Set<CustomerTablePaymentOperation>().SingleAsync();
        receipt.AttemptId.Should().Be(attempt.Id); receipt.OperationId.Should().Be(intent.OperationId);
        receipt.PayloadHash.Should().Be(prepared.Proof.PayloadHash); receipt.IsStopped.Should().BeFalse();
        Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        WithdrawNative();
        (await PaymentResolutionAsync(await SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto
        { Intent = intent, Proof = prepared.Proof }))).Should().Be(first);
        (await PaymentResolutionAsync(await SendAsync(Path + "recover", prepared.Proof))).Should().Be(first);
        (await PaymentResolutionAsync(await SendAsync(Path + "stop", prepared.Proof))).Should().Be(first);
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(1);
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        Gateway.Invocations.Should().NotContain(call => call.Method.Name != "get_IsConfigured");
    }

    [Fact]
    public async Task Equivalent_reordered_item_units_keep_the_same_original_review_and_reservation()
    {
        var intent = await PaymentIntentAsync(AccountPaymentMode.Items);
        await using var context = DatabaseFixture.CreateContext();
        var item = await context.OrderItems.SingleAsync();
        intent = intent with { SelectedUnits = [new(item.OrderId, item.Id, 2), new(item.OrderId, item.Id, 1)] };
        var prepared = await PreparedPaymentAsync(intent);
        var submitted = intent with { SelectedUnits = intent.SelectedUnits.Reverse().ToArray() };
        var result = await PaymentResolutionAsync(await SendAsync(Path + "submit",
            new CustomerTablePaymentSubmissionDto { Intent = submitted, Proof = prepared.Proof }));
        result.Outcome.Should().Be(CustomerTablePaymentOutcome.Applied);
        (await context.AccountPaymentAttempts.SingleAsync()).AmountMinor.Should().Be(1400);
        (await PaymentResolutionAsync(await SendAsync(Path + "recover", prepared.Proof))).Should().Be(result);
    }

    [Fact]
    public async Task Unseen_is_read_only_and_stopping_after_expiry_permanently_seals_a_late_submit()
    {
        var intent = await PaymentIntentAsync();
        var prepared = await PreparedPaymentAsync(intent);
        var unseen = await PaymentResolutionAsync(await SendAsync(Path + "recover", prepared.Proof));
        unseen.Outcome.Should().Be(CustomerTablePaymentOutcome.Unseen); unseen.AttemptId.Should().BeNull();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TableServiceSessionStatus.Closed));
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var stopped = await PaymentResolutionAsync(await SendAsync(Path + "stop", prepared.Proof));
        stopped.Outcome.Should().Be(CustomerTablePaymentOutcome.Stopped); stopped.AttemptId.Should().BeNull();
        (await PaymentResolutionAsync(await SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto
        { Intent = intent, Proof = prepared.Proof }))).Should().Be(stopped);
        (await PaymentResolutionAsync(await SendAsync(Path + "recover", prepared.Proof))).Should().Be(stopped);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("participant")]
    [InlineData("feature")]
    [InlineData("body")]
    public async Task Refusal_cannot_leave_an_orphan_quote_reservation_or_receipt(string changed)
    {
        var intent = await PaymentIntentAsync();
        var prepared = await PreparedPaymentAsync(intent);
        await using var context = DatabaseFixture.CreateContext();
        if (changed == "revision") await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(row => row.AccountRevision, row => row.AccountRevision + 1));
        if (changed == "participant") Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        if (changed == "feature") WithdrawNative();
        if (changed == "body") intent = intent with { Mode = AccountPaymentMode.Amount, AmountMinor = 350 };
        (await SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto
        { Intent = intent, Proof = prepared.Proof })).IsSuccessStatusCode.Should().BeFalse();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        await NoPaymentEffectsAsync();
    }

    [Fact]
    public async Task Concurrent_replays_accept_only_one_shared_reservation()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var request = new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof };
        var responses = await Task.WhenAll(SendAsync(Path + "submit", request), SendAsync(Path + "submit", request));
        var first = await PaymentResolutionAsync(responses[0]);
        (await PaymentResolutionAsync(responses[1])).Should().Be(first);
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(1);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Original_proof_cannot_be_replaced_by_a_fresh_preparation_using_the_same_public_id()
    {
        var intent = await PaymentIntentAsync();
        var first = await PreparedPaymentAsync(intent); var other = await PreparedPaymentAsync(intent);
        first.Proof.PayloadHash.Should().NotBe(other.Proof.PayloadHash);
        (await PaymentResolutionAsync(await SendAsync(Path + "stop", first.Proof))).Outcome.Should().Be(CustomerTablePaymentOutcome.Stopped);
        (await SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = other.Proof }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("hash")]
    [InlineData("ciphertext")]
    public async Task Foreign_or_changed_recovery_cannot_read_or_seal_original_work(string changed)
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var proof = changed switch
        {
            "tenant" => prepared.Proof with { TenantId = Guid.NewGuid() },
            "operation" => prepared.Proof with { OperationId = Guid.NewGuid() },
            "hash" => prepared.Proof with { PayloadHash = new string('a', 64) },
            "ciphertext" => prepared.Proof with { OwnerProof = "invalid" },
            _ => prepared.Proof
        };
        var session = changed == "session" ? Guid.NewGuid().ToString("D") : null;
        (await SendAsync(Path + "recover", proof, session)).IsSuccessStatusCode.Should().BeFalse();
        (await SendAsync(Path + "stop", proof, session)).IsSuccessStatusCode.Should().BeFalse();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        await NoPaymentEffectsAsync();
    }


}
