using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePlanAuthorityTests(DatabaseFixture fixture) : CustomerTablePlanFixture(fixture)
{
    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("hash")]
    [InlineData("ciphertext")]
    [InlineData("customer")]
    [InlineData("invalid-bearer")]
    public async Task Changed_or_foreign_original_authority_cannot_read_or_stop_a_plan(string changed)
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var proof = changed switch
        {
            "tenant" => prepared.Proof with { TenantId = Guid.NewGuid() },
            "operation" => prepared.Proof with { OperationId = Guid.NewGuid() },
            "hash" => prepared.Proof with { PayloadHash = new string('a', 64) },
            "ciphertext" => prepared.Proof with { OwnerProof = "invalid" },
            _ => prepared.Proof
        };
        if (changed == "customer") await SignInCustomerAsync();
        if (changed == "invalid-bearer") Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        var session = changed == "session" ? Guid.NewGuid().ToString("D") : null;
        (await SendAsync(PlanPath + "recover", proof, session)).IsSuccessStatusCode.Should().BeFalse();
        (await SendAsync(PlanPath + "stop", proof, session)).IsSuccessStatusCode.Should().BeFalse();
        await NoPlansAsync();
    }
    [Theory]
    [InlineData("intent")]
    [InlineData("proof")]
    [InlineData("extra")]
    public async Task Strict_submission_refuses_missing_objects_and_extra_fields(string changed)
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var body = JsonSerializer.SerializeToNode(new CustomerTablePlanSubmissionDto(intent, prepared.Proof), JsonOptions)!.AsObject();
        if (changed == "extra") body["tipMinor"] = 1; else body[changed] = null;
        (await SendAsync(PlanPath + "submit", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NoPlansAsync();
    }
    [Fact]
    public async Task Same_public_id_with_another_preparation_cannot_replace_a_permanent_decision()
    {
        var intent = await PlanIntentAsync(); var original = await PreparePlanAsync(intent); var other = await PreparePlanAsync(intent);
        original.Proof.PayloadHash.Should().NotBe(other.Proof.PayloadHash);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "stop", original.Proof))).Outcome.Should().Be("stopped");
        (await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, other.Proof))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(0);
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("custom")]
    public async Task Missing_or_changed_retained_plan_never_becomes_unseen_or_replacement(string changed)
    {
        var intent = await PlanIntentAsync(2, [1000, 400]); var prepared = await PreparePlanAsync(intent);
        var request = new CustomerTablePlanSubmissionDto(intent, prepared.Proof);
        await PlanResolutionAsync(await SendAsync(PlanPath + "submit", request));
        await using var context = DatabaseFixture.CreateContext();
        if (changed == "missing") await context.AccountEqualSharePlans.ExecuteDeleteAsync();
        if (changed == "scope") await context.AccountEqualSharePlans.ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeJson, "[]"));
        if (changed == "custom") await context.AccountEqualSharePlans.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomAmountsJson, "[400,1000]"));
        (await SendAsync(PlanPath + "recover", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(PlanPath + "stop", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(PlanPath + "submit", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Failed_supersession_rolls_back_invalidation_and_the_new_plan_together()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        var original = await PlanResolutionAsync(await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof)));
        var next = intent with { OperationId = Guid.NewGuid(), SupersedesPlanId = original.PlanId, ShareCount = 2 };
        var nextPrepared = await PreparePlanAsync(next); FailAfterPlan = true;
        (await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(next, nextPrepared.Proof))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountEqualSharePlans.SingleAsync()).InvalidatedAt.Should().BeNull();
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(1);
        FailAfterPlan = false;
        var applied = await PlanResolutionAsync(await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(next, nextPrepared.Proof)));
        applied.PlanId.Should().NotBe(original.PlanId.GetValueOrDefault()); (await context.AccountEqualSharePlans.CountAsync()).Should().Be(2);
        (await context.AccountEqualSharePlans.AsNoTracking().SingleAsync(row => row.Id == original.PlanId)).InvalidatedAt.Should().NotBeNull();
    }
    [Fact]
    public async Task Contribution_proof_cannot_be_used_as_a_split_plan_proof()
    {
        var payment = await PaymentIntentAsync(); var contribution = await PreparedPaymentAsync(payment);
        var proof = new CustomerTablePlanProofDto(1, contribution.Proof.TenantId, contribution.Proof.OperationId,
            contribution.Proof.PayloadHash, contribution.Proof.OwnerProof);
        (await SendAsync(PlanPath + "stop", proof)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await NoPlansAsync();
    }
    [Theory]
    [InlineData("zero")]
    [InlineData("sum")]
    [InlineData("count")]
    [InlineData("overflow")]
    public async Task Invalid_custom_distribution_cannot_get_a_preparation(string changed)
    {
        var intent = await PlanIntentAsync(2, changed switch
        { "zero" => [0, 1400], "sum" => [400, 400], "count" => [1400], _ => [long.MaxValue, 1] });
        (await SendAsync(PlanPath + "prepare", intent)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NoPlansAsync();
    }
}
