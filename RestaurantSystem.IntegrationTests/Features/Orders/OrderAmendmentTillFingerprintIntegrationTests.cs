using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentTillResolutionIntegrationTests
{
    [Fact]
    public async Task Source_tender_change_after_start_keeps_the_accepted_refund_held()
    {
        AuthenticateAsAdmin();
        var operation = await StartManualRefundAsync();
        await using (var mutation = DatabaseFixture.CreateContext())
        {
            await mutation.OrderPayments.Where(value => value.Id == _paymentId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Amount, 21m));
        }

        using var confirmation = await Client.PostAsJsonAsync(ConfirmPath(operation.OperationId),
            new ManualTillConfirmationRequest { PaymentId = _paymentId, TillReference = "cash-return-43" },
            JsonOptions);
        confirmation.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the accepted refund snapshot no longer matches its source tender");

        await using var verify = DatabaseFixture.CreateContext();
        var savedOperation = await verify.OrderAmendmentResolutionOperations
            .Include(value => value.Legs).SingleAsync(value => value.Id == operation.OperationId);
        savedOperation.State.Should().NotBe(OrderAmendmentResolutionOperationState.Resolved);
        savedOperation.Legs.Single().State.Should().Be(OrderAmendmentRefundLegState.Succeeded);
        savedOperation.Legs.Single().ManualTillReference.Should().Be("cash-return-43");
        (await verify.OrderAmendmentRefundEvidence.CountAsync(value =>
            value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation)).Should().Be(1);
        (await verify.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId)).Should().Be(0);
        (await verify.Orders.SingleAsync(value => value.Id == _orderId))
            .BillingCreditAmount.Should().Be(0m);
        var payment = await verify.OrderPayments.SingleAsync(value => value.Id == _paymentId);
        payment.Amount.Should().Be(21m);
        payment.RefundedAmount.Should().BeNull();
    }

    private async Task<OrderAmendmentResolutionResultDto> StartManualRefundAsync()
    {
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = 1,
            Currency = "CHF",
            ManualRefunds = [new ManualRefundSelectionRequest { PaymentId = _paymentId, AmountMinor = 1000 }]
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        using var startResponse = await Client.PostAsJsonAsync(path,
            new OrderAmendmentResolutionStartRequest
            {
                Quote = request,
                QuoteHash = quote.QuoteHash,
                ExpiresAt = quote.ExpiresAt
            }, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!.Result!;
    }
}
