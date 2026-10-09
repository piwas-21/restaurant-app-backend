using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountCashRefundIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_validates_receipt_backed_cash_history_through_resolved_target_and_later_tail(
        bool resolveLaterTail)
    {
        var target = _cases[0];
        var later = _cases[1];
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var orders = await seed.Orders.Where(value => value.Id == target.OrderId || value.Id == later.OrderId)
                .ToListAsync();
            orders.Should().HaveCount(2);
            foreach (var order in orders)
                order.Status = OrderStatus.Pending;
            await seed.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var targetStarted = await StartCashRefundAsync(target, confirm: true, returnedMinor: 5);
        var laterOperation = await StartCashRefundAsync(later, confirm: resolveLaterTail, returnedMinor: 0);

        await using var current = DatabaseFixture.CreateContext();
        var expectedVersion = await current.Orders.Where(value => value.Id == target.OrderId)
            .Select(value => value.Version).SingleAsync();
        using var cancel = await Client.PostAsJsonAsync($"/api/orders/{target.OrderId}/cancel",
            new CancelOrderCommand
            {
                OrderId = target.OrderId,
                ExpectedVersion = expectedVersion,
                CancellationReason = "Settled cash refund history"
            }, JsonOptions);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancel.Content.ReadFromJsonAsync<ApiResponse<RestaurantSystem.Api.Features.Orders.Dtos.OrderDto>>(
            JsonOptions))!.Success.Should().BeTrue();

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.Where(value => value.Id == target.OrderId)
            .Select(value => value.Status).SingleAsync()).Should().Be(OrderStatus.Cancelled);
        var targetPayment = await verify.OrderPayments.SingleAsync(value => value.Id == target.PaymentId);
        targetPayment.Status.Should().Be(PaymentStatus.Refunded);
        targetPayment.IsRefunded.Should().BeTrue();
        targetPayment.RefundedAmount.Should().Be(0.01m);
        var targetReversal = await verify.AccountPaymentAllocationReversals
            .SingleAsync(value => value.OrderId == target.OrderId);
        targetReversal.AmountMinor.Should().Be(target.ExactMinor);
        var targetOperation = await verify.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == targetStarted.OperationId);
        targetOperation.State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        var laterSaved = await verify.OrderAmendmentResolutionOperations
            .Include(value => value.Legs).SingleAsync(value => value.Id == laterOperation.OperationId);
        laterSaved.State.Should().Be(resolveLaterTail
            ? OrderAmendmentResolutionOperationState.Resolved
            : OrderAmendmentResolutionOperationState.Processing);
        laterSaved.Legs.Should().ContainSingle(value => value.State == (resolveLaterTail
            ? OrderAmendmentRefundLegState.Succeeded : OrderAmendmentRefundLegState.Pending));
        (await verify.AccountCashCollectionReceipts.SingleAsync(value => value.AttemptId == _attemptId))
            .ReceivedMinor.Should().Be(400);
    }

    private async Task<(Guid OperationId, long ExactMinor)> StartCashRefundAsync(
        CashRefundCase item, bool confirm, long returnedMinor)
    {
        var basePath = $"/api/staff/orders/{item.OrderId}/amendments/{item.AmendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{basePath}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await contextResponse.Content.ReadAsStringAsync());
        var context = (await contextResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = context.ExpectedOrderVersion,
            ExpectedAccountRevision = context.ExpectedAccountRevision,
            Currency = "CHF",
            ManualRefunds = []
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await quoteResponse.Content.ReadAsStringAsync());
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.RefundLegs.Should().ContainSingle(value => value.Custody == "ManualTill"
            && value.AmountMinor == item.ExactMinor && value.RequiresTillConfirmation);

        using var startResponse = await Client.PostAsJsonAsync(basePath,
            new OrderAmendmentResolutionStartRequest
            {
                Quote = request,
                QuoteHash = quote.QuoteHash,
                ExpiresAt = quote.ExpiresAt
            }, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await startResponse.Content.ReadAsStringAsync());
        var started = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        started.Outcome.Should().Be("accepted");
        started.Result!.State.Should().Be("Processing");

        if (confirm)
        {
            using var confirmation = await Client.PostAsJsonAsync(
                $"/api/staff/amendment-financial-resolution-operations/{started.Result.OperationId}/confirm-till",
                new ManualTillConfirmationRequest
                {
                    PaymentId = item.PaymentId,
                    TillReference = $"cash-prefix-{item.OrderId:N}",
                    CashReturnedMinor = returnedMinor
                }, JsonOptions);
            confirmation.StatusCode.Should().Be(HttpStatusCode.OK,
                await confirmation.Content.ReadAsStringAsync());
            var result = (await confirmation.Content.ReadFromJsonAsync<
                ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
            result.State.Should().Be("Resolved");
            _ = result.RefundLegs.Should().ContainSingle(value => value.State == "Succeeded");
        }

        return (started.Result.OperationId, item.ExactMinor);
    }
}
