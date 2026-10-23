using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePlanLimitTests(DatabaseFixture fixture) : CustomerTablePlanFixture(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_native_limit_refuses_new_work_but_retains_original_stop(bool afterPreparation)
    {
        var intent = await PlanIntentAsync();
        var prepared = afterPreparation ? await PreparePlanAsync(intent) : null;
        Limits.MaximumNativePlanShares = 2;
        var response = prepared is null ? await SendAsync(PlanPath + "prepare", intent)
            : await SendAsync(PlanPath + "submit", new CustomerTablePlanSubmissionDto(intent, prepared.Proof));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        PlanObserved.Should().BeFalse();
        await NoPlansAsync();
        if (prepared is null) return;
        (await PlanResolutionAsync(await SendAsync(PlanPath + "recover", prepared.Proof))).Outcome.Should().Be("unseen");
        (await PlanResolutionAsync(await SendAsync(PlanPath + "stop", prepared.Proof))).Outcome.Should().Be("stopped");
    }

    [Fact]
    public async Task Configured_native_limit_allows_its_exact_boundary()
    {
        var intent = await PlanIntentAsync(count: 2);
        Limits.MaximumNativePlanShares = 2;
        var prepared = await PreparePlanAsync(intent);
        prepared.SharesMinor.Should().Equal(700, 700);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "submit",
            new CustomerTablePlanSubmissionDto(intent, prepared.Proof)))).Outcome.Should().Be("applied");
        await NoPaymentEffectsAsync();
    }

    [Fact]
    public async Task Lowered_native_limit_preserves_applied_original_recovery_stop_and_replay()
    {
        var intent = await PlanIntentAsync();
        var prepared = await PreparePlanAsync(intent);
        var submission = new CustomerTablePlanSubmissionDto(intent, prepared.Proof);
        var applied = await PlanResolutionAsync(await SendAsync(PlanPath + "submit", submission));
        Limits.MaximumNativePlanShares = 2;
        WithdrawNative();
        Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        (await PlanResolutionAsync(await SendAsync(PlanPath + "recover", prepared.Proof))).Should().Be(applied);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "stop", prepared.Proof))).Should().Be(applied);
        (await PlanResolutionAsync(await SendAsync(PlanPath + "submit", submission))).Should().Be(applied);
        await NoPaymentEffectsAsync();
    }

    private AccountPaymentSettings Limits => Factory.Services.GetRequiredService<IOptions<AccountPaymentSettings>>().Value;
}
