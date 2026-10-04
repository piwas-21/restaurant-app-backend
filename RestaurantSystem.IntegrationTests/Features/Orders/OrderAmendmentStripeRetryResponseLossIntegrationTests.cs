using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Lost_retry_create_response_is_held_on_retry_attempt_and_exact_replay_resolves_once()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("failed");
        _refundState.CreateStatuses.Enqueue("succeeded");
        _refundState.LoseCreateResponseOnCalls.Add(2);
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        using var failedResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        failedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var failed = (await failedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        failed.Outcome.Should().Be("accepted");
        failed.Result!.State.Should().Be("ReconciliationRequired");
        failed.Result.RefundLegs.Should().ContainSingle(value => value.State == "Failed");

        using var lostRetryResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        lostRetryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var held = (await lostRetryResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        held.Outcome.Should().Be("accepted");
        held.Result!.OperationId.Should().Be(failed.Result.OperationId);
        held.Result.State.Should().Be("ReconciliationRequired",
            "the timeout belongs to the durable retry attempt, not the failed prior attempt");
        held.Result.RefundLegs.Should().ContainSingle(value => value.State == "ReconciliationRequired");
        _refundState.CreateCalls.Should().Be(2);
        _refundState.CreatedEvidence.Should().HaveCount(2);

        Guid retryAttemptId;
        await using (var beforeRecovery = DatabaseFixture.CreateContext())
        {
            var operation = await beforeRecovery.OrderAmendmentResolutionOperations
                .SingleAsync(value => value.Id == failed.Result.OperationId);
            operation.State.Should().Be(OrderAmendmentResolutionOperationState.ReconciliationRequired);
            var leg = await beforeRecovery.OrderAmendmentRefundLegs.Include(value => value.Attempts)
                .SingleAsync(value => value.OperationId == operation.Id);
            leg.State.Should().Be(OrderAmendmentRefundLegState.ReconciliationRequired);
            var attempts = leg.Attempts.OrderBy(value => value.Sequence).ToArray();
            attempts.Should().HaveCount(2);
            retryAttemptId = attempts[1].Id;

            var observations = await beforeRecovery.OrderAmendmentRefundEvidence
                .Where(value => value.RefundLegId == leg.Id
                    && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
                .OrderBy(value => value.Sequence).ToArrayAsync();
            observations.Should().HaveCount(2);
            observations[0].RefundAttemptId.Should().Be(attempts[0].Id);
            observations[0].ProviderRefundStatus.Should().Be("failed");
            observations[0].FailureCode.Should().Be("provider_refund_failed");
            observations[1].RefundAttemptId.Should().Be(retryAttemptId);
            observations[1].ProviderRefundId.Should().BeNull();
            observations[1].ProviderRefundStatus.Should().BeNull();
            observations[1].FailureCode.Should().Be("provider_outcome_unknown");
            observations[1].State.Should().Be(OrderAmendmentRefundLegState.ReconciliationRequired);
            (await beforeRecovery.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId))
                .Should().Be(0);
            (await beforeRecovery.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
            (await beforeRecovery.OrderPayments.SingleAsync(value => value.Id == _paymentId))
                .RefundedAmount.Should().BeNull();
        }

        using var recoveredResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        recoveredResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovered = (await recoveredResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        recovered.Outcome.Should().Be("accepted");
        recovered.Result!.OperationId.Should().Be(failed.Result.OperationId);
        recovered.Result.State.Should().Be("Resolved");
        recovered.Result.RefundLegs.Should().ContainSingle(value => value.State == "Succeeded");
        _refundState.CreateCalls.Should().Be(2, "the accepted retry is adopted, never recreated");

        await using var final = DatabaseFixture.CreateContext();
        var finalLeg = await final.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleAsync(value => value.OperationId == recovered.Result.OperationId);
        var finalAttempts = finalLeg.Attempts.OrderBy(value => value.Sequence).ToArray();
        finalAttempts.Should().HaveCount(2);
        finalAttempts[1].Id.Should().Be(retryAttemptId);
        finalLeg.State.Should().Be(OrderAmendmentRefundLegState.Succeeded);
        var finalObservations = await final.OrderAmendmentRefundEvidence
            .Where(value => value.RefundLegId == finalLeg.Id
                && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
                && value.ProviderRefundId != null)
            .OrderBy(value => value.Sequence).ToArrayAsync();
        finalObservations.Select(value => value.ProviderRefundStatus).Should().Equal("failed", "succeeded");
        finalObservations.Select(value => value.RefundAttemptId)
            .Should().Equal(finalAttempts[0].Id, retryAttemptId);
        finalObservations.Select(value => value.ProviderRefundId).Distinct().Should().HaveCount(2);
        (await final.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId)).Should().Be(1);
        (await final.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
        (await final.OrderPayments.SingleAsync(value => value.Id == _paymentId)).RefundedAmount.Should().Be(10m);
    }
}
