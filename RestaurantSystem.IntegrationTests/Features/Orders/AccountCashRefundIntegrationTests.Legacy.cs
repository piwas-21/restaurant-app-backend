using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountCashRefundIntegrationTests
{
    private async Task<CashRefundCase> AddLegacyCashCaseAsync()
    {
        var now = FixedNow.UtcDateTime;
        var attemptId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var revision = await context.TableServiceSessions.Where(value => value.Id == _sessionId)
            .Select(value => value.AccountRevision).SingleAsync();
        var seeded = BuildCase(1000, "legacy", now, attemptId);
        var allocation = seeded.Allocation;
        var snapshot = new AccountPaymentQuoteSnapshot(
            revision, AccountPaymentMode.Amount, PaymentMethod.Cash, 1000, "CHF",
            now.AddHours(1), null, null,
            [new AccountPaymentAllocationDto(allocation.OrderId, allocation.OrderItemId,
                allocation.StartOrdinal, allocation.UnitCount, allocation.MinorPerUnit,
                allocation.AmountMinor)],
            CashSettlement: null);
        var attempt = new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 2,
            ExpectedAccountRevision = revision,
            AmountMinor = 1000,
            Currency = "CHF",
            PayloadHash = new string('d', 64),
            SnapshotJson = AccountPaymentSnapshots.Serialize(snapshot),
            QuoteExpiresAt = snapshot.QuoteExpiresAt,
            CompletedAt = now,
            CreatedAt = now.AddMinutes(-1),
            CreatedBy = "legacy-cashier:test",
            Allocations = [allocation]
        };

        context.Orders.Add(seeded.Order);
        context.OrderAmendments.Add(seeded.Amendment);
        context.AccountPaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return new CashRefundCase(seeded.Order.Id, seeded.Amendment.Id,
            seeded.Payment.Id, seeded.Item.Id, seeded.ExactMinor);
    }

    private async Task ResolveLegacyCashRefundWithoutPhysicalReceiptAsync(CashRefundCase legacy)
    {
        var basePath = $"/api/staff/orders/{legacy.OrderId}/amendments/{legacy.AmendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{basePath}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = context.ExpectedOrderVersion,
            ExpectedAccountRevision = context.ExpectedAccountRevision,
            Currency = context.Currency,
            ManualRefunds = []
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.RefundLegs.Should().ContainSingle().Which.CashRefund.Should().BeNull();

        using var startResponse = await Client.PostAsJsonAsync(basePath,
            new OrderAmendmentResolutionStartRequest
            {
                Quote = request,
                QuoteHash = quote.QuoteHash,
                ExpiresAt = quote.ExpiresAt
            }, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var operation = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!.Result!;
        operation.RefundLegs.Should().ContainSingle().Which.CashRefund.Should().BeNull();

        using var confirmationResponse = await Client.PostAsJsonAsync(
            $"/api/staff/amendment-financial-resolution-operations/{operation.OperationId}/confirm-till",
            new ManualTillConfirmationRequest
            {
                PaymentId = legacy.PaymentId,
                TillReference = "legacy-cash-return"
            }, JsonOptions);
        confirmationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await confirmationResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        var leg = result.RefundLegs.Should().ContainSingle().Subject;
        leg.AmountMinor.Should().Be(legacy.ExactMinor);
        leg.CashRefund.Should().BeNull();
        leg.CashReturn.Should().BeNull();
        leg.TillConfirmation.Should().NotBeNull();
    }
}
