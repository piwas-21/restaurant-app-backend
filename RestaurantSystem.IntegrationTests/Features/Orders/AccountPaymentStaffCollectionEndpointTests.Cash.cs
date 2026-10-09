using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountPaymentStaffCollectionEndpointTests
{
    [Fact]
    public async Task Server_cash_collection_requires_opt_in_and_records_the_reviewed_tender_after_opt_out()
    {
        var account = await Seed();
        using var disabledFactory = Factory(payments: true, optIn: false);
        using var disabledServer = Client(disabledFactory, "Server");
        var deniedQuote = CashQuote();

        using var denied = await disabledServer.PostAsJsonAsync(
            $"{Route(account.SessionId)}/quotes", deniedQuote);
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var context = fixture.CreateContext())
        {
            (await context.AccountPaymentAttempts.CountAsync()).Should().Be(0);
        }

        using var enabledFactory = Factory(payments: true, optIn: true);
        using var enabledServer = Client(enabledFactory, "Server");
        var quoteRequest = CashQuote();
        using var quoteResponse = await enabledServer.PostAsJsonAsync(
            $"{Route(account.SessionId)}/quotes", quoteRequest);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        quote.AmountMinor.Should().Be(333);
        quote.CashSettlement.Should().Be(new CashSettlementQuote(
            "chf-cash-5-rappen-v1", "CHF", PaymentMethod.Cash, 333, 2, 335));

        var operationRoute = $"{Route(account.SessionId)}/operations/{quote.OperationId}";
        using var reserve = await enabledServer.PostAsJsonAsync($"{operationRoute}/reserve",
            new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 1 });
        reserve.StatusCode.Should().Be(HttpStatusCode.OK);

        var incomplete = new CaptureAccountPaymentRequest { ExpectedVersion = 2 };
        using var missingReceived = await disabledServer.PostAsJsonAsync($"{operationRoute}/collect", incomplete);
        missingReceived.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var shortReceived = await disabledServer.PostAsJsonAsync($"{operationRoute}/collect",
            incomplete with { ReceivedMinor = 334 });
        shortReceived.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.AccountPaymentAttempts.SingleAsync(
                value => value.OperationId == quote.OperationId);
            attempt.State.Should().Be(AccountPaymentState.Reserved);
            (await context.OrderPayments.CountAsync()).Should().Be(0);
            (await context.AccountCashCollectionReceipts.CountAsync()).Should().Be(0);
        }

        var captureRequest = incomplete with { ReceivedMinor = 335 };
        using var capturedResponse = await disabledServer.PostAsJsonAsync(
            $"{operationRoute}/collect", captureRequest);
        capturedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var captured = (await capturedResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        captured.State.Should().Be(AccountPaymentState.Captured);
        captured.CashReceipt.Should().NotBeNull();

        using var replayResponse = await disabledServer.PostAsJsonAsync(
            $"{operationRoute}/collect", captureRequest);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        replay.CashReceipt.Should().Be(captured.CashReceipt);

        await using var verify = fixture.CreateContext();
        var attemptAfterReplay = await verify.AccountPaymentAttempts.SingleAsync(
            value => value.OperationId == quote.OperationId);
        var persisted = await verify.AccountCashCollectionReceipts.SingleAsync(
            value => value.AttemptId == attemptAfterReplay.Id);
        persisted.ActorRole.Should().Be(UserRole.Server);
        persisted.ExactAmountMinor.Should().Be(333);
        persisted.AdjustmentMinor.Should().Be(2);
        persisted.DueAmountMinor.Should().Be(335);
        persisted.ReceivedMinor.Should().Be(335);
        persisted.ChangeMinor.Should().Be(0);
        attemptAfterReplay.State.Should().Be(AccountPaymentState.Captured);
        attemptAfterReplay.Version.Should().Be(3);
        (await verify.OrderPayments.CountAsync()).Should().Be(1);
        (await verify.OrderPayments.SingleAsync()).Amount.Should().Be(3.33m);
    }

    private static CreateAccountPaymentQuoteRequest CashQuote() => new()
    {
        OperationId = Guid.NewGuid(),
        ExpectedAccountRevision = 1,
        Mode = AccountPaymentMode.Amount,
        PaymentMethod = PaymentMethod.Cash,
        AmountMinor = 333
    };
}
