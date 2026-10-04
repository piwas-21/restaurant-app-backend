using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountCashRefundIntegrationTests
{
    [Fact]
    public async Task Cash_refund_history_spans_orders_and_requires_the_exact_physical_return_attestation()
    {
        AuthenticateAsAdmin();
        var expectedCashRefunds = new long[] { 5, 0, 330 };
        var expectedPriorExact = new long[] { 0, 1, 3 };
        var expectedPriorCash = new long[] { 0, 5, 5 };
        var returnedTotal = 0L;

        for (var index = 0; index < _cases.Length; index++)
        {
            var item = _cases[index];
            var basePath = $"/api/staff/orders/{item.OrderId}/amendments/{item.AmendmentId}/financial-resolution";
            using var contextResponse = await Client.GetAsync($"{basePath}/context");
            contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var context = (await contextResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
            context.Currency.Should().Be("CHF");

            var quoteRequest = new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = Guid.NewGuid(),
                ExpectedOrderVersion = context.ExpectedOrderVersion,
                ExpectedAccountRevision = context.ExpectedAccountRevision,
                Currency = context.Currency,
                ManualRefunds = []
            };
            using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", quoteRequest, JsonOptions);
            quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var quote = (await quoteResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
            var legQuote = quote.RefundLegs.Should().ContainSingle().Subject;
            legQuote.AmountMinor.Should().Be(item.ExactMinor);
            legQuote.CashRefund.Should().Be(new CashRefundQuoteDto(
                "chf-cash-5-rappen-v1", 333, 335,
                expectedPriorExact[index], expectedPriorCash[index], item.ExactMinor,
                expectedCashRefunds[index] - item.ExactMinor, expectedCashRefunds[index],
                333 - expectedPriorExact[index] - item.ExactMinor,
                AccountCashSettlementPolicy.RetainedDue(
                    new CashSettlementQuote("chf-cash-5-rappen-v1", "CHF", PaymentMethod.Cash,
                        333, 2, 335),
                    333 - expectedPriorExact[index] - item.ExactMinor)));

            var startRequest = new OrderAmendmentResolutionStartRequest
            {
                Quote = quoteRequest,
                QuoteHash = quote.QuoteHash,
                ExpiresAt = quote.ExpiresAt
            };
            using var startResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
            startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var operation = (await startResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!.Result!;
            operation.State.Should().Be("Processing");
            var operationLeg = operation.RefundLegs.Should().ContainSingle().Subject;
            operationLeg.CashRefund.Should().Be(legQuote.CashRefund);
            operationLeg.CashReturn.Should().BeNull();

            await AssertUnresolvedCashReportAsync(item.ExactMinor, expectedCashRefunds[index]);

            var confirmPath = $"/api/staff/amendment-financial-resolution-operations/{operation.OperationId}/confirm-till";
            var missingAttestation = new ManualTillConfirmationRequest
            {
                PaymentId = item.PaymentId,
                TillReference = $"cash-return-{index + 1}"
            };
            using var missingResponse = await Client.PostAsJsonAsync(confirmPath, missingAttestation, JsonOptions);
            missingResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "the operator must explicitly attest the physical amount, including zero");

            using var incorrectResponse = await Client.PostAsJsonAsync(confirmPath,
                missingAttestation with { CashReturnedMinor = expectedCashRefunds[index] + 1 }, JsonOptions);
            incorrectResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "the attestation must match the physical amount frozen by the quote");

            using (var beforeConfirmation = DatabaseFixture.CreateContext())
                (await beforeConfirmation.AccountCashRefundEvidence.CountAsync()).Should().Be(index);

            var confirmation = missingAttestation with { CashReturnedMinor = expectedCashRefunds[index] };
            using var confirmationResponse = await Client.PostAsJsonAsync(confirmPath, confirmation, JsonOptions);
            confirmationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = (await confirmationResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
            result.State.Should().Be("Resolved");
            var resultLeg = result.RefundLegs.Should().ContainSingle().Subject;
            resultLeg.AmountMinor.Should().Be(item.ExactMinor);
            resultLeg.CashRefund.Should().Be(legQuote.CashRefund);
            resultLeg.CashReturn.Should().Be(new CashReturnEvidenceDto(
                item.ExactMinor, expectedCashRefunds[index] - item.ExactMinor,
                expectedCashRefunds[index], resultLeg.CashReturn!.ConfirmedAt));
            returnedTotal += resultLeg.CashReturn!.CashReturnedMinor;

            using var retryResponse = await Client.PostAsJsonAsync(confirmPath, confirmation, JsonOptions);
            retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var retry = (await retryResponse.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
            retry.RefundLegs.Single().CashReturn.Should().Be(resultLeg.CashReturn);
        }

        returnedTotal.Should().Be(335);
        var legacy = await AddLegacyCashCaseAsync();
        await ResolveLegacyCashRefundWithoutPhysicalReceiptAsync(legacy);
        await AssertPersistedCashHistoryAsync(expectedCashRefunds, expectedPriorExact, expectedPriorCash, legacy);
        await AssertAccountCashReportAsync();
    }

    private async Task AssertPersistedCashHistoryAsync(
        IReadOnlyList<long> cashRefunds, IReadOnlyList<long> priorExact,
        IReadOnlyList<long> priorCash, CashRefundCase legacy)
    {
        await using var context = DatabaseFixture.CreateContext();
        var receipt = await context.AccountCashCollectionReceipts.AsNoTracking().SingleAsync();
        receipt.ExactAmountMinor.Should().Be(333);
        receipt.DueAmountMinor.Should().Be(335);
        receipt.ReceivedMinor.Should().Be(400);
        receipt.ChangeMinor.Should().Be(65);
        receipt.ActorRole.Should().Be(UserRole.Cashier);

        var intents = await context.AccountCashRefundIntents.AsNoTracking()
            .OrderBy(value => value.PreviouslyRefundedExactMinor).ToArrayAsync();
        var evidence = await context.AccountCashRefundEvidence.AsNoTracking().ToArrayAsync();
        intents.Should().HaveCount(3);
        evidence.Should().HaveCount(3);
        intents.Select(value => value.PreviouslyRefundedExactMinor).Should().Equal(priorExact);
        intents.Select(value => value.PreviouslyRefundedCashMinor).Should().Equal(priorCash);
        intents.Select(value => value.ExactRefundAmountMinor).Should().Equal(_cases.Select(value => value.ExactMinor));
        intents.Select(value => value.CashRefundAmountMinor).Should().Equal(cashRefunds);
        evidence.Sum(value => value.ExactRefundAmountMinor).Should().Be(333);
        evidence.Sum(value => value.CashReturnedMinor).Should().Be(335);
        evidence.Sum(value => value.RefundAdjustmentMinor).Should().Be(2);
        evidence.Should().OnlyContain(value => value.ActorRole == UserRole.Admin);

        var payments = await context.OrderPayments.AsNoTracking()
            .Where(value => _cases.Select(item => item.PaymentId).Append(legacy.PaymentId).Contains(value.Id))
            .ToArrayAsync();
        payments.Sum(value => decimal.ToInt64(value.RefundedAmount!.Value * 100m)).Should().Be(1333);
        payments.Should().OnlyContain(value => value.Status == PaymentStatus.Refunded);
        var reversals = await context.AccountPaymentAllocationReversals.AsNoTracking().ToArrayAsync();
        reversals.Sum(value => value.AmountMinor).Should().Be(1333);
        reversals.Should().HaveCount(4);
    }

    private async Task AssertUnresolvedCashReportAsync(long exactMinor, long physicalMinor)
    {
        await using var context = DatabaseFixture.CreateContext();
        var handler = new GetZReportQueryHandler(context, new FixedTenantClock("UTC"),
            NullLogger<GetZReportQueryHandler>.Instance);
        // Today's pending return must remain visible even when the requested report date has no movements.
        var response = await handler.Handle(new GetZReportQuery(_reportDate.AddDays(-1)), CancellationToken.None);
        var movement = response.Data!.AccountCashMovements!;
        movement.ByCurrency.Should().BeEmpty();
        var unresolved = movement.UnresolvedByCurrency.Should().ContainSingle().Subject;
        unresolved.Currency.Should().Be("CHF");
        unresolved.UnresolvedReturnCount.Should().Be(1);
        unresolved.UnresolvedExactRefundMinor.Should().Be(exactMinor);
        unresolved.UnconfirmedPhysicalCashMinor.Should().Be(physicalMinor);
    }

    private async Task AssertAccountCashReportAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var handler = new GetZReportQueryHandler(context, new FixedTenantClock("UTC"),
            NullLogger<GetZReportQueryHandler>.Instance);
        var response = await handler.Handle(new GetZReportQuery(_reportDate),
            CancellationToken.None);

        response.Data.Should().NotBeNull();
        response.Data!.Refunds.TotalRefundedAmount.Should().Be(13.33m);
        response.Data.AccountCashMovements.Should().NotBeNull();
        var movement = response.Data.AccountCashMovements!;
        movement.CoversWholeRestaurantTill.Should().BeFalse();
        movement.Coverage.Should().Contain("not the whole restaurant till");
        movement.ByCurrency.Should().ContainSingle();
        var currency = movement.ByCurrency.Single();
        currency.Currency.Should().Be("CHF");
        currency.CollectionCount.Should().Be(1);
        currency.CollectedExactMinor.Should().Be(333);
        currency.CashDueMinor.Should().Be(335);
        currency.CashReceivedMinor.Should().Be(400);
        currency.ChangeReturnedMinor.Should().Be(65);
        currency.ReturnCount.Should().Be(3);
        currency.ExactRefundedMinor.Should().Be(333);
        currency.PhysicalCashReturnedMinor.Should().Be(335);
        currency.RefundAdjustmentMinor.Should().Be(2);
        currency.LegacyCaptureWithoutReceiptCount.Should().Be(1);
        currency.LegacyCaptureExactMinor.Should().Be(1000);
        currency.LegacyReturnWithoutPhysicalEvidenceCount.Should().Be(1);
        currency.LegacyExactRefundMinor.Should().Be(1000);
        movement.UnresolvedByCurrency.Should().BeEmpty();
    }
}
