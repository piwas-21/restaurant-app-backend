using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Delayed_pending_create_response_cannot_regress_a_later_succeeded_recovery()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("pending");
        var responseGate = new FakeAmendmentRefundResponseGate();
        _refundState.CreateResponseGate = responseGate;
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        var delayedResponse = Client.PostAsJsonAsync(basePath, request, JsonOptions);
        var prepared = await responseGate.ResponsePrepared.WaitAsync(TimeSpan.FromSeconds(30));
        var operationId = Guid.Empty;
        try
        {
            _refundState.SetRefundStatus(prepared.RefundId, "succeeded");
            using var currentResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
            currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var current = (await currentResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
            current.Outcome.Should().Be("accepted");
            current.Result!.State.Should().Be("Resolved");
            operationId = current.Result.OperationId;
        }
        finally
        {
            responseGate.Release();
        }

        using var delayed = await delayedResponse;
        delayed.StatusCode.Should().Be(HttpStatusCode.OK);
        var delayedResult = (await delayed.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        delayedResult.Outcome.Should().Be("accepted");
        delayedResult.Result!.State.Should().Be("Resolved");
        delayedResult.Result.OperationId.Should().Be(operationId);
        _features.OrderAmendmentsV1 = false;
        var recoveryPath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/operations/{_clientOperationId}";
        using var recovery = await Client.GetAsync(recoveryPath);
        recovery.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovered = (await recovery.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        recovered.Outcome.Should().Be("accepted");
        recovered.Result!.OperationId.Should().Be(operationId);
        recovered.Result.State.Should().Be("Resolved");
        _refundState.CreateCalls.Should().Be(1);

        await using var readback = DatabaseFixture.CreateContext();
        var operation = await readback.OrderAmendmentResolutionOperations
            .Include(value => value.Legs).SingleAsync(value => value.ClientOperationId == _clientOperationId);
        operation.State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        operation.Legs.Should().ContainSingle(value => value.State == OrderAmendmentRefundLegState.Succeeded);
        var observations = await readback.OrderAmendmentRefundEvidence
            .Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
                && value.ProviderRefundId != null).ToListAsync();
        observations.Should().ContainSingle(value => value.ProviderRefundStatus == "succeeded");
        (await readback.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId))
            .Should().Be(1);
        (await readback.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delayed_succeeded_verification_after_finalization_keeps_the_resolved_result()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("succeeded");
        var firstVerification = new FakeAmendmentRefundListResponseGate();
        var delayedVerification = new FakeAmendmentRefundListResponseGate();
        _refundState.ListResponseGates[2] = firstVerification;
        _refundState.ListResponseGates[3] = delayedVerification;
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        var firstRequest = Client.PostAsJsonAsync(basePath, request, JsonOptions);
        _ = await firstVerification.ResponsePrepared.WaitAsync(TimeSpan.FromSeconds(30));
        var delayedRequest = Client.PostAsJsonAsync(basePath, request, JsonOptions);
        try
        {
            _ = await delayedVerification.ResponsePrepared.WaitAsync(TimeSpan.FromSeconds(30));
            firstVerification.Release();
            using var firstResponse = await firstRequest;
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var first = (await firstResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
            first.Result!.State.Should().Be("Resolved");
            delayedVerification.Release();
            using var delayedResponse = await delayedRequest;
            delayedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var delayed = (await delayedResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
            delayed.Result!.OperationId.Should().Be(first.Result.OperationId);
            delayed.Result.State.Should().Be("Resolved");
        }
        finally
        {
            firstVerification.Release();
            delayedVerification.Release();
        }

        await using var readback = DatabaseFixture.CreateContext();
        var operation = await readback.OrderAmendmentResolutionOperations
            .Include(value => value.Legs)
            .SingleAsync(value => value.ClientOperationId == _clientOperationId);
        operation.State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        operation.Legs.Should().ContainSingle(value => value.State == OrderAmendmentRefundLegState.Succeeded);
        (await readback.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId))
            .Should().Be(1);
        (await readback.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Pending_refund_can_transition_to_succeeded_through_the_real_checkout_evidence_writer()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("pending");
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        using var initial = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        initial.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = (await initial.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        pending.Outcome.Should().Be("accepted");
        pending.Result!.State.Should().Be("Processing");
        pending.Result.RefundLegs.Should().ContainSingle(value => value.State == "Pending");
        _refundState.CreateCalls.Should().Be(1);
        await using (var pendingHistory = DatabaseFixture.CreateContext())
        {
            var observation = await pendingHistory.OrderAmendmentRefundEvidence.SingleAsync(value =>
                value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation);
            observation.FailureCode.Should().Be("provider_refund_pending");
        }

        await RecordCheckoutEvidenceAsync();
        await using (var captured = DatabaseFixture.CreateContext())
        {
            var journal = await captured.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.ProviderRefundedMinor.Should().Be(0);
            journal.ReconciliationRequired.Should().BeFalse(
                "a canonical pending refund is not a capture-proof failure");
            (await captured.OrderBillingCredits.CountAsync()).Should().Be(0);
            (await captured.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
        }

        var refundId = _refundState.ReadRefunds().Should().ContainSingle().Which.RefundId;
        _refundState.SetRefundStatus(refundId, "succeeded");
        await RecordCheckoutEvidenceAsync();
        await using (var observed = DatabaseFixture.CreateContext())
        {
            var journal = await observed.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.ProviderRefundedMinor.Should().Be(1000);
            journal.ReconciliationRequired.Should().BeFalse();
        }

        _features.OrderAmendmentsV1 = false;
        using var replay = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolved = (await replay.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        resolved.Outcome.Should().Be("accepted");
        resolved.Result!.OperationId.Should().Be(pending.Result.OperationId);
        resolved.Result.State.Should().Be("Resolved");
        _refundState.CreateCalls.Should().Be(1, "status recovery adopts the original provider refund");

        await using var final = DatabaseFixture.CreateContext();
        (await final.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        var reversal = await final.AccountPaymentAllocationReversals.SingleAsync();
        reversal.UnitCount.Should().Be(1);
        reversal.AmountMinor.Should().Be(1000);
        (await final.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId))
            .ReconciliationRequired.Should().BeFalse();
        var resolvedLeg = await final.OrderAmendmentRefundLegs
            .SingleAsync(value => value.OperationId == resolved.Result.OperationId);
        var history = await final.OrderAmendmentRefundEvidence
            .Where(value => value.RefundLegId == resolvedLeg.Id)
            .OrderBy(value => value.Sequence).ToListAsync();
        history.Where(value => value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)
            .Select(value => value.ProviderRefundStatus).Should().Equal("pending", "succeeded");
    }

    [Fact]
    public async Task Successful_refund_can_recover_after_a_temporary_canonical_total_mismatch()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("succeeded");
        _refundState.CanonicalRefundedMinorOverride = 0;
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        using var heldResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        heldResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var held = (await heldResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        held.Outcome.Should().Be("accepted");
        held.Result!.State.Should().Be("ReconciliationRequired");
        held.Result.RefundLegs.Should().ContainSingle(value => value.State == "Succeeded");
        _refundState.CreateCalls.Should().Be(1);

        await using (var heldContext = DatabaseFixture.CreateContext())
        {
            (await heldContext.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId))
                .Should().Be(0);
            (await heldContext.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
            (await heldContext.OrderPayments.SingleAsync(value => value.Id == _paymentId))
                .RefundedAmount.Should().BeNull();
        }

        _refundState.CanonicalRefundedMinorOverride = null;
        using var recoveredResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        recoveredResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovered = (await recoveredResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        recovered.Outcome.Should().Be("accepted");
        recovered.Result!.OperationId.Should().Be(held.Result.OperationId);
        recovered.Result.State.Should().Be("Resolved");
        _refundState.CreateCalls.Should().Be(1, "recovery must adopt the exact original provider refund");

        _refundState.CanonicalRefundedMinorOverride = 0;
        using var replayResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replayed.Result!.OperationId.Should().Be(held.Result.OperationId);
        replayed.Result.State.Should().Be("Resolved");
        _refundState.CreateCalls.Should().Be(1);

        await using var final = DatabaseFixture.CreateContext();
        (await final.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        (await final.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
        (await final.OrderAmendmentResolutionOperations.SingleAsync(value => value.Id == held.Result.OperationId))
            .State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        (await final.OrderAmendmentRefundEvidence.CountAsync(value =>
            value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
            && value.ProviderRefundStatus == "succeeded")).Should().Be(1);
    }

    [Fact]
    public async Task Failed_provider_refund_can_retry_once_and_finalization_keeps_both_refund_id_histories()
    {
        _refundState.LoseFirstCreateResponse = false;
        _refundState.CreateStatuses.Enqueue("failed");
        _refundState.CreateStatuses.Enqueue("succeeded");
        AuthenticateAsAdmin();
        var (basePath, request) = await BuildStartRequestAsync();

        using var failedResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        failedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var failed = (await failedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        failed.Outcome.Should().Be("accepted");
        failed.Result!.State.Should().Be("ReconciliationRequired");
        failed.Result.RefundLegs.Should().ContainSingle(value => value.State == "Failed");
        _refundState.CreateCalls.Should().Be(1);
        await using (var failedHistory = DatabaseFixture.CreateContext())
        {
            var observation = await failedHistory.OrderAmendmentRefundEvidence.SingleAsync(value =>
                value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation);
            observation.FailureCode.Should().Be("provider_refund_failed");
        }

        await using (var held = DatabaseFixture.CreateContext())
        {
            (await held.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId)).Should().Be(0);
            (await held.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
            (await held.OrderPayments.SingleAsync(value => value.Id == _paymentId)).RefundedAmount.Should().BeNull();
        }

        using var retriedResponse = await Client.PostAsJsonAsync(basePath, request, JsonOptions);
        var retryBody = await retriedResponse.Content.ReadAsStringAsync();
        retriedResponse.StatusCode.Should().Be(HttpStatusCode.OK, "response was {0}", retryBody);
        var retried = System.Text.Json.JsonSerializer.Deserialize<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(retryBody, JsonOptions)!.Data!;
        retried.Outcome.Should().Be("accepted");
        retried.Result!.OperationId.Should().Be(failed.Result.OperationId);
        retried.Result.State.Should().Be("Resolved");
        _refundState.CreateCalls.Should().Be(2);
        _refundState.ReadRefunds().Should().HaveCount(2);

        await using var final = DatabaseFixture.CreateContext();
        var leg = await final.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleAsync(value => value.OperationId == retried.Result.OperationId);
        leg.Attempts.Should().HaveCount(2);
        leg.State.Should().Be(OrderAmendmentRefundLegState.Succeeded);
        var observations = await final.OrderAmendmentRefundEvidence
            .Where(value => value.RefundLegId == leg.Id
                && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
                && value.ProviderRefundId != null)
            .OrderBy(value => value.Sequence).ToListAsync();
        observations.Select(value => value.ProviderRefundStatus).Should().Equal("failed", "succeeded");
        observations.Select(value => value.FailureCode).Should().Equal("provider_refund_failed", null);
        observations.Select(value => value.ProviderRefundId).Distinct().Should().HaveCount(2);
        (await final.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
        (await final.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        var reversal = await final.AccountPaymentAllocationReversals.SingleAsync();
        await AssertImmutableHistoryAsync(ImmutableHistoryTable.RefundAttempts,
            leg.Attempts.OrderBy(value => value.Sequence).First().Id);
        await AssertImmutableHistoryAsync(ImmutableHistoryTable.RefundEvidence, observations[0].Id);
        await AssertImmutableHistoryAsync(ImmutableHistoryTable.AllocationReversals, reversal.Id);
        await AssertRollbackRetainsPaidHistoryAsync(retried.Result.OperationId);
    }

    private async Task<(string BasePath, OrderAmendmentResolutionStartRequest Request)> BuildStartRequestAsync()
    {
        var basePath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        return (basePath, new OrderAmendmentResolutionStartRequest
        {
            Quote = quoteRequest,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        });
    }

    private async Task RecordCheckoutEvidenceAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
        var evidence = await new FakeAmendmentCheckoutEvidenceReader(_refundState)
            .ReadAsync(journal, false, CancellationToken.None);
        await new AccountCheckoutEvidenceWriter(context, Options.Create(new AccountCheckoutSettings()),
            TimeProvider.System).RecordAsync(journal, evidence, CancellationToken.None);
    }

    private async Task AssertImmutableHistoryAsync(ImmutableHistoryTable table, Guid id)
    {
        var commands = table switch
        {
            ImmutableHistoryTable.RefundAttempts => new[]
            {
                "UPDATE order_amendment_refund_attempts SET created_by = 'attempted-mutation' WHERE id = @id",
                "DELETE FROM order_amendment_refund_attempts WHERE id = @id"
            },
            ImmutableHistoryTable.RefundEvidence => new[]
            {
                "UPDATE order_amendment_refund_evidence SET created_by = 'attempted-mutation' WHERE id = @id",
                "DELETE FROM order_amendment_refund_evidence WHERE id = @id"
            },
            ImmutableHistoryTable.AllocationReversals => new[]
            {
                "UPDATE account_payment_allocation_reversals SET created_by = 'attempted-mutation' WHERE id = @id",
                "DELETE FROM account_payment_allocation_reversals WHERE id = @id"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        foreach (var sql in commands)
        {
            await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            var execute = async () => await command.ExecuteNonQueryAsync();
            var exception = (await execute.Should().ThrowAsync<PostgresException>()).Which;
            exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            exception.MessageText.Should().Be("Paid amendment financial history is immutable");
        }
    }

    private enum ImmutableHistoryTable
    {
        RefundAttempts,
        RefundEvidence,
        AllocationReversals
    }

    private async Task AssertRollbackRetainsPaidHistoryAsync(Guid operationId)
    {
        try
        {
            var rollback = () => MigrateAsync("20261003120021_AddTableVisitReadiness");
            var exception = (await rollback.Should().ThrowAsync<PostgresException>()).Which;
            exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            exception.MessageText.Should().Be("Paid amendment financial history must be retained");
            (await LatestAppliedMigrationAsync()).Should().Be("20261003153304_AddOrderAmendmentResolutionRefusals");
            (await TableExistsAsync("order_amendment_resolution_refusals")).Should().BeTrue();

            await using var verify = DatabaseFixture.CreateContext();
            var operation = await verify.OrderAmendmentResolutionOperations
                .Include(value => value.Legs).ThenInclude(value => value.Attempts)
                .SingleAsync(value => value.Id == operationId);
            operation.Legs.Should().ContainSingle();
            operation.Legs.Single().Attempts.Should().HaveCount(2);
            (await verify.OrderAmendmentRefundEvidence.CountAsync()).Should().BeGreaterThan(0);
            (await verify.AccountPaymentAllocationReversals.CountAsync()).Should().Be(1);
            (await verify.OrderAmendmentResolutionRefusals.CountAsync()).Should().Be(0);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = DatabaseFixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task<string> LatestAppliedMigrationAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
