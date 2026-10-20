using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationAcceptanceTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Fact]
    public async Task Original_guest_cancellation_is_atomic_replayable_and_leaves_financial_evidence_untouched()
    {
        var request = await AwaitingDelayAsync();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await context.Orders.AsNoTracking().SingleAsync();
        var snapshot = await context.OrderBillingSnapshots.AsNoTracking().SingleAsync();
        var tender = await context.OrderPayments.AsNoTracking().SingleAsync();
        var response = await CancelAsync(request);
        response.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        response.ObservedVersion.Should().Be(request.Intent.ExpectedVersion + 1);
        response.Reason.Should().BeNull();
        (await CancelAsync(request)).Should().BeEquivalentTo(response);
        (await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/original/recover", request)))
            .Should().BeEquivalentTo(response);
        var order = await context.Orders.AsNoTracking().SingleAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.Version.Should().Be(response.ObservedVersion);
        order.LastChangeSequence.Should().BeGreaterThan(before.LastChangeSequence);
        order.Total.Should().Be(before.Total);
        order.TotalPaid.Should().Be(before.TotalPaid);
        order.RemainingAmount.Should().Be(before.RemainingAmount);
        order.PaymentStatus.Should().Be(before.PaymentStatus);
        order.FidelityPointsRedeemed.Should().Be(before.FidelityPointsRedeemed);
        order.FidelityPointsEarned.Should().Be(before.FidelityPointsEarned);
        (await context.OrderBillingSnapshots.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(snapshot);
        (await context.OrderPayments.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(tender);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
        (await context.OrderStatusHistories.CountAsync(row => row.ToStatus == OrderStatus.Cancelled)).Should().Be(1);
        (await DecisionsAsync()).Should().ContainSingle();
        CancellationNotifications.Verify(value => value.NotifyAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Authenticated_customer_history_and_original_proof_share_the_same_terminal_decision()
    {
        var request = await AwaitingDelayAsync(customer: true);
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(
            await Client.GetAsync($"/api/customer-mobile/orders/{request.Intent.OrderId}/cancellation?schemaVersion=1"));
        preparation.Allowed.Should().BeTrue();
        preparation.PayloadHash.Should().Be(request.Intent.PayloadHash);
        var response = await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/submit", request.Intent));
        response.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        (await CancelAsync(request)).Should().BeEquivalentTo(response);
        (await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/recover", request.Intent)))
            .Should().BeEquivalentTo(response);
        (await DecisionsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Unseen_recovery_is_read_only_and_does_not_prevent_a_late_original_submission()
    {
        var request = await AwaitingDelayAsync();
        var result = await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/original/recover", request));
        result.Outcome.Should().Be(CustomerOrderCancellationOutcome.Unseen);
        result.ObservedVersion.Should().BeNull();
        result.Reason.Should().BeNull();
        (await DecisionsAsync()).Should().BeEmpty();
        var cancelled = await CancelAsync(request);
        cancelled.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        (await CancelAsync(request)).Should().BeEquivalentTo(cancelled);
    }

    [Fact]
    public async Task Refused_version_is_terminal_even_when_the_order_later_becomes_eligible_again()
    {
        var request = await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, (_, order) => { order.Notes = "Restaurant updated"; return Task.CompletedTask; });
        var result = await CancelAsync(request);
        result.Outcome.Should().Be(CustomerOrderCancellationOutcome.Refused);
        result.Reason.Should().Be(CustomerOrderCancellationReason.OrderChanged);
        await ChangeAsync(request.Intent.OrderId, (_, order) => { order.Notes = "Later update"; return Task.CompletedTask; });
        (await CancelAsync(request)).Should().BeEquivalentTo(result);
        (await DecisionsAsync()).Should().ContainSingle();
        CancellationNotifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Notification_failure_after_commit_cannot_reclassify_the_saved_cancellation()
    {
        var request = await AwaitingDelayAsync();
        CancellationNotifications.Setup(value => value.NotifyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Notification unavailable"));
        var result = await CancelAsync(request);
        result.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        (await CancelAsync(request)).Should().BeEquivalentTo(result);
        CancellationNotifications.Verify(value => value.NotifyAsync(request.Intent.OrderId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Mobile_withdrawal_freezes_a_refusal_and_does_not_hide_its_later_recovery()
    {
        var request = await AwaitingDelayAsync();
        var settings = Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        settings.Enabled = false;
        var refused = await CancelAsync(request);
        refused.Outcome.Should().Be(CustomerOrderCancellationOutcome.Refused);
        refused.Reason.Should().Be(CustomerOrderCancellationReason.OrderingUnavailable);
        settings.Enabled = true;
        (await CancelAsync(request)).Should().BeEquivalentTo(refused);
        (await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/original/recover", request)))
            .Should().BeEquivalentTo(refused);
        CancellationNotifications.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_identical_or_distinct_operations_create_one_business_effect(bool sameOperation)
    {
        var first = await AwaitingDelayAsync();
        var second = sameOperation ? first : first with { Intent = first.Intent with { OperationId = Guid.NewGuid() } };
        var results = await Task.WhenAll(CancelAsync(first), CancelAsync(second));
        if (sameOperation) results[0].Should().BeEquivalentTo(results[1]);
        else results.Count(value => value.Outcome == CustomerOrderCancellationOutcome.Cancelled).Should().Be(1);
        var decisions = await DecisionsAsync();
        decisions.Should().HaveCount(sameOperation ? 1 : 2);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.OrderStatusHistories.CountAsync(value => value.ToStatus == OrderStatus.Cancelled)).Should().Be(1);
        CancellationNotifications.Verify(value => value.NotifyAsync(first.Intent.OrderId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
