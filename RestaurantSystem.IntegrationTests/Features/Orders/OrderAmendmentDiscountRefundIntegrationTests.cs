using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Discounted_unit_refund_reverses_its_captured_eight_euros_and_keeps_the_other_unit_due()
    {
        Guid allocationId;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.Include(value => value.Items).Include(value => value.Payments)
                .SingleAsync(value => value.Id == _orderId);
            var food = BillDiscountMath.Food(order, new("CHF"));
            order.BillChargeAmount = -4m;
            order.BillChargesJson = JsonSerializer.Serialize(new[] { new OrderBillCharge(Guid.NewGuid(), "discount",
                "Discount", -4m, "CHF", DateTime.UtcNow, "test", 4m, "Reduce", "Fixed", 2000,
                BillDiscountMath.Allocate(food, 400)) }, BillChargeRules.JsonOptions);
            order.TotalPaid = 8m;
            order.RemainingAmount = 8m;
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
            order.Payments.Single().Amount = 8m;
            var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .SingleAsync(value => value.Id == _attemptId);
            attempt.AmountMinor = 800;
            var allocation = attempt.Allocations.Single();
            allocationId = allocation.Id;
            allocation.UnitCount = 1;
            allocation.MinorPerUnit = 800;
            allocation.AmountMinor = 800;
            var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.AmountMinor = 800;
            journal.ProviderCapturedMinor = 800;
            journal.CreatePayloadHash = AccountCheckoutReplayPayload.Hash(new AccountStripeCheckoutRequest
            {
                AttemptId = _attemptId,
                Context = new AccountStripeContext(journal.ProviderAccountId, false),
                AmountMinor = 800,
                Currency = "CHF",
                ExpiresAt = journal.ExpiresAt,
                IdempotencyKey = journal.CreateIdempotencyKey,
                ReturnBaseUrl = journal.ReturnBaseUrl
            });
            var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
            var preview = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson);
            amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(preview with
            {
                RemovedUnitValueMinor = 800,
                PotentialCreditMinor = 800,
                NetAccountDeltaMinor = -800
            });
            await context.SaveChangesAsync();
        }
        _refundState.LoseFirstCreateResponse = false;
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        await using var versions = DatabaseFixture.CreateContext();
        var version = await versions.Orders.Where(value => value.Id == _orderId).Select(value => value.Version).SingleAsync();
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = version,
            ExpectedAccountRevision = 1,
            Currency = "CHF"
        };
        var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK, await quoteResponse.Content.ReadAsStringAsync());
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(800);
        quote.RefundLegs.Should().ContainSingle(value => value.AmountMinor == 800);
        var started = await Client.PostAsJsonAsync(path, new OrderAmendmentResolutionStartRequest
        {
            Quote = request,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        }, JsonOptions);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var result = (await started.Content.ReadFromJsonAsync<ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!.Result!;
        result.State.Should().Be("Resolved");
        await using var verify = DatabaseFixture.CreateContext();
        var original = await verify.AccountPaymentAllocations.SingleAsync(value => value.Id == allocationId);
        original.AmountMinor.Should().Be(800);
        original.StartOrdinal.Should().Be(1);
        original.UnitCount.Should().Be(1);
        var reversal = await verify.AccountPaymentAllocationReversals.SingleAsync(value => value.AllocationId == allocationId);
        reversal.AmountMinor.Should().Be(800);
        var saved = await verify.Orders.SingleAsync(value => value.Id == _orderId);
        saved.PayableTotal.Should().Be(8m);
        saved.BillingCreditAmount.Should().Be(8m);
        saved.TotalPaid.Should().Be(0m);
        saved.RemainingAmount.Should().Be(8m);
        (await new AccountDebtSnapshotReader(verify).ReadAsync(_sessionId, CancellationToken.None))
            .Debt.AvailableMinor.Should().Be(800);
    }
}
