using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePlanOperationTests(DatabaseFixture fixture) : CustomerTablePlanFixture(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_equal_or_custom_review_is_read_only_then_one_original_plan_survives_withdrawal(bool custom)
    {
        var intent = await PlanIntentAsync(custom ? 2 : 3, custom ? [1000, 400] : []);
        var prepared = await PreparePlanAsync(intent);
        prepared.TotalMinor.Should().Be(1400); prepared.Currency.Should().Be("CHF");
        prepared.SharesMinor.Should().Equal(custom ? [1000L, 400L] : [465L, 465L, 470L]);
        prepared.RoundingIncrementMinor.Should().Be(custom ? 1 : 5);
        prepared.Scope.Should().ContainSingle().Which.Should().Match<AccountPaymentAllocationDto>(row => row.UnitCount == 2 && row.MinorPerUnit == 700 && row.AmountMinor == 1400);
        await NoPlansAsync();
        (await PlanResolutionAsync(await SendAsync(PlanPath + "recover", prepared.Proof))).Outcome.Should().Be("unseen");
        var submission = new CustomerTablePlanSubmissionDto(intent, prepared.Proof);
        var applied = await PlanResolutionAsync(await SendAsync(PlanPath + "submit", submission));
        applied.Outcome.Should().Be("applied"); applied.PlanId.Should().NotBeNull();
        await using var context = DatabaseFixture.CreateContext();
        var plan = await context.AccountEqualSharePlans.SingleAsync();
        plan.Id.Should().Be(applied.PlanId.GetValueOrDefault()); plan.OperationId.Should().NotBe(intent.OperationId);
        plan.TotalMinor.Should().Be(1400); plan.ShareCount.Should().Be(custom ? 2 : 3);
        plan.RoundingIncrementMinor.Should().Be(custom ? 1 : 5);
        var receipt = await context.Set<CustomerTablePlanOperation>().SingleAsync();
        receipt.PlanId.Should().Be(plan.Id); receipt.PayloadHash.Should().Be(prepared.Proof.PayloadHash);
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        (await PlanResolutionAsync(await SendAsync(PlanPath + "recover", prepared.Proof))).Should().Be(applied);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "stop", prepared.Proof))).Should().Be(applied);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "submit", submission))).Should().Be(applied);
        (await context.AccountEqualSharePlans.SingleAsync()).InvalidatedAt.Should().BeNull();
        await NoPaymentEffectsAsync();
    }
    [Fact]
    public async Task Unseen_stop_survives_expired_participant_and_permanently_refuses_late_submit()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        await using var context = DatabaseFixture.CreateContext();
        await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var stopped = await PlanResolutionAsync(await SendAsync(PlanPath + "stop", prepared.Proof));
        stopped.Outcome.Should().Be("stopped"); stopped.PlanId.Should().BeNull();
        (await PlanResolutionAsync(await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof)))).Should().Be(stopped);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "recover", prepared.Proof))).Should().Be(stopped);
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(0);
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
        await NoPaymentEffectsAsync();
    }
    [Theory]
    [InlineData("revision")]
    [InlineData("participant")]
    [InlineData("rollout")]
    [InlineData("limits")]
    [InlineData("body")]
    public async Task First_submit_revalidates_original_review_without_leaving_a_plan(string changed)
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        await using var context = DatabaseFixture.CreateContext();
        if (changed == "revision") await context.TableServiceSessions.ExecuteUpdateAsync(update => update.SetProperty(row => row.AccountRevision, row => row.AccountRevision + 1));
        if (changed == "participant") Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        if (changed == "rollout") WithdrawNative();
        if (changed == "limits") Gateway.SetupGet(value => value.IsConfigured).Returns(false);
        if (changed == "body") intent = intent with { ShareCount = 2 };
        (await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof))).IsSuccessStatusCode.Should().BeFalse();
        await NoPlansAsync();
    }
    [Fact]
    public async Task Rollout_withdrawal_after_read_only_review_withholds_the_preparation()
    {
        var intent = await PlanIntentAsync(); AfterPlanProof = WithdrawNative;
        (await SendAsync(PlanPath + "prepare", intent)).IsSuccessStatusCode.Should().BeFalse();
        await NoPlansAsync();
    }
    [Fact]
    public async Task Concurrent_original_submissions_commit_one_plan_and_one_decision()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var request = new CustomerTablePlanSubmissionDto(intent, prepared.Proof);
        var responses = await Task.WhenAll(SendAsync(PlanPath + "submit", request), SendAsync(PlanPath + "submit", request));
        (await PlanResolutionAsync(responses[0])).Should().Be(await PlanResolutionAsync(responses[1]));
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(1);
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
        await NoPaymentEffectsAsync();
    }
    [Fact]
    public async Task Stop_racing_submit_serializes_to_one_immutable_decision()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var responses = await Task.WhenAll(SendAsync(PlanPath + "stop", prepared.Proof), SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof)));
        var result = await PlanResolutionAsync(responses[0]);
        (await PlanResolutionAsync(responses[1])).Should().Be(result);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(result.Outcome == "applied" ? 1 : 0);
    }
    [Fact]
    public async Task Failure_after_real_plan_creation_rolls_back_before_the_same_original_retry()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var request = new CustomerTablePlanSubmissionDto(intent, prepared.Proof); FailAfterPlan = true;
        (await SendAsync(PlanPath + "submit", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        PlanObserved.Should().BeTrue(); await NoPlansAsync(); FailAfterPlan = false;
        (await PlanResolutionAsync(await SendAsync(PlanPath + "submit", request))).Outcome.Should().Be("applied");
    }
}
