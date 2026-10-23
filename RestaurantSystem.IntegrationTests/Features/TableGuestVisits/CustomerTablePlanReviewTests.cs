using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePlanReviewTests(DatabaseFixture fixture) : CustomerTablePlanFixture(fixture)
{
    [Fact]
    public async Task Same_total_with_changed_debt_scope_is_refused_before_shared_plan_creation()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        // Accepted charge roots are immutable in PostgreSQL; inject hostile shared projection input.
        ReviewChange = review => review with
        {
            Scope = review.Scope.Select(row =>
            row with { StartOrdinal = row.StartOrdinal + 1 }).ToArray()
        };
        prepared.TotalMinor.Should().Be(1400);
        (await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        PlanObserved.Should().BeFalse(); await NoPlansAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_reviewed_share_must_satisfy_current_contribution_limits(bool afterPreparation)
    {
        var intent = await PlanIntentAsync();
        var prepared = afterPreparation ? await PreparePlanAsync(intent) : null;
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<AccountOnlineContributionSettings>>().Value.MinimumAmountMinor = 700;
        var response = prepared is null ? await SendAsync(PlanPath + "prepare", intent)
            : await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        PlanObserved.Should().BeFalse(); await NoPlansAsync();
    }
    [Fact]
    public async Task Changed_intent_cannot_claim_the_original_stopped_decision()
    {
        var intent = await PlanIntentAsync(); var prepared = await PreparePlanAsync(intent);
        await PlanResolutionAsync(await SendAsync(PlanPath + "stop", prepared.Proof));
        (await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent with { ShareCount = 2 }, prepared.Proof)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("tenantId")]
    [InlineData("operationId")]
    [InlineData("serviceSessionId")]
    [InlineData("expectedAccountRevision")]
    [InlineData("shareCount")]
    [InlineData("customAmountsMinor")]
    [InlineData("extra")]
    public async Task Preparation_requires_explicit_wire_identity_and_distribution(string field)
    {
        var intent = await PlanIntentAsync(); var body = JsonSerializer.SerializeToNode(intent, JsonOptions)!.AsObject();
        if (field == "extra") body["amountMinor"] = 1400; else body.Remove(field).Should().BeTrue();
        (await SendAsync(PlanPath + "prepare", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NoPlansAsync();
    }
}
