using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Mixed_provider_and_till_refund_posts_only_after_both_legs_are_proven()
    {
        var mixedTender = await PrepareMixedTenderAsync();
        var manualPaymentId = mixedTender.PaymentId;
        AuthenticateAsAdmin();
        var basePath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = mixedTender.OrderVersion,
            ExpectedAccountRevision = mixedTender.AccountRevision,
            Currency = "CHF"
        };

        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", request, JsonOptions);
        var quoteBody = await quoteResponse.Content.ReadAsStringAsync();
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK, "quote response was {0}", quoteBody);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(1000);
        quote.RefundMinor.Should().Be(1000);
        quote.RefundLegs.Should().HaveCount(2);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == _paymentId
            && value.Custody == "StripeDirect" && value.AmountMinor == 600);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == manualPaymentId
            && value.Custody == "ManualTill" && value.AmountMinor == 400 && value.RequiresTillConfirmation);

        var startRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = request,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        };
        using var startResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        start.Outcome.Should().Be("accepted");
        var operation = start.Result!;
        operation.State.Should().Be("ReconciliationRequired");
        _refundState.CreateCalls.Should().Be(1);

        _features.OrderAmendmentsV1 = false;
        using var replayResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the original durable refund can be recovered while new starts are disabled");
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("accepted");
        replay.Result!.OperationId.Should().Be(operation.OperationId);
        replay.Result.State.Should().Be("Processing");
        replay.Result.RefundLegs.Should().ContainSingle(value => value.PaymentId == _paymentId
            && value.State == "Succeeded");
        replay.Result.RefundLegs.Should().ContainSingle(value => value.PaymentId == manualPaymentId
            && value.State == "Pending");
        _refundState.CreateCalls.Should().Be(1, "recovery adopts the existing provider refund identity");

        await RecordCanonicalCheckoutRefundAsync();
        await AssertMixedOperationStillHeld(operation.OperationId, manualPaymentId);

        using var tillResponse = await Client.PostAsJsonAsync(
            $"/api/staff/amendment-financial-resolution-operations/{operation.OperationId}/confirm-till",
            new ManualTillConfirmationRequest
            {
                PaymentId = manualPaymentId,
                TillReference = "cash-refund-2026-10-03"
            }, JsonOptions);
        tillResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var settled = (await tillResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        settled.State.Should().Be("Resolved");
        settled.RefundLegs.Should().OnlyContain(value => value.State == "Succeeded");
        _refundState.CreateCalls.Should().Be(1);

        await using var final = DatabaseFixture.CreateContext();
        var savedOperation = await final.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operation.OperationId);
        savedOperation.State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        var reversals = await final.AccountPaymentAllocationReversals.ToArrayAsync();
        reversals.Should().HaveCount(2);
        reversals.Should().ContainSingle(value => value.StartOrdinal == 1
            && value.UnitCount == 1 && value.AmountMinor == 600);
        reversals.Should().ContainSingle(value => value.StartOrdinal == 1
            && value.UnitCount == 1 && value.AmountMinor == 400);
        reversals.Sum(value => value.AmountMinor).Should().Be(1000);
        (await final.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        var order = await final.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == _orderId);
        order.TotalPaid.Should().Be(10m);
        order.BillingCreditAmount.Should().Be(10m);
        order.RemainingAmount.Should().Be(0m);
        order.Payments.Single(value => value.Id == _paymentId).RefundedAmount.Should().Be(6m);
        order.Payments.Single(value => value.Id == manualPaymentId).RefundedAmount.Should().Be(4m);
    }

    private async Task<(Guid PaymentId, int OrderVersion, long AccountRevision)> PrepareMixedTenderAsync()
    {
        var manualPaymentId = Guid.NewGuid();
        var manualItemId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.Items)
            .SingleAsync(value => value.Id == _orderId);
        var providerItem = order.Items.Single(value => value.Id == _itemId);
        providerItem.UnitPrice = 6m;
        providerItem.ItemTotal = 12m;
        var manualItem = new OrderItem
        {
            Id = manualItemId,
            OrderId = _orderId,
            ProductName = "Manual tender meal",
            Quantity = 2,
            UnitPrice = 4m,
            ItemTotal = 8m,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        manualItem.Order = order;
        context.OrderItems.Add(manualItem);
        var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
        var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(
            amendment.ChangesJson);
        changes[0] = changes[0] with
        {
            Previous = new OrderItemDto
            {
                Id = _itemId,
                ProductName = providerItem.ProductName,
                Quantity = providerItem.Quantity,
                UnitPrice = providerItem.UnitPrice,
                ItemTotal = providerItem.ItemTotal
            }
        };
        changes.Add(new OrderAmendmentChangeSnapshot(manualItemId,
            OrderAmendmentChangeKind.Void, 1, 1, false, new OrderItemDto
            {
                Id = manualItemId,
                ProductName = manualItem.ProductName,
                Quantity = manualItem.Quantity,
                UnitPrice = manualItem.UnitPrice,
                ItemTotal = manualItem.ItemTotal
            }, null));
        amendment.ChangesJson = OrderAmendmentJson.Serialize(changes);

        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == _attemptId);
        attempt.AmountMinor = 1200;
        var allocation = attempt.Allocations.Single();
        allocation.MinorPerUnit = 600;
        allocation.AmountMinor = 1200;
        attempt.SnapshotJson = ManualSnapshot(
            AccountPaymentMode.Amount, PaymentMethod.OnlinePayment, 1200, "CHF", _orderId,
            _itemId, 600);
        var providerPayment = await context.OrderPayments.SingleAsync(value => value.Id == _paymentId);
        providerPayment.Amount = 12m;
        context.OrderPayments.Add(new OrderPayment
        {
            Id = manualPaymentId,
            OrderId = _orderId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 8m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        });
        var manualAttemptId = Guid.NewGuid();
        context.AccountPaymentAttempts.Add(new AccountPaymentAttempt
        {
            Id = manualAttemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.GuestParticipant,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            AmountMinor = 800,
            Currency = "CHF",
            PayloadHash = new string('d', 64),
            SnapshotJson = ManualSnapshot(
                AccountPaymentMode.Amount, PaymentMethod.Cash, 800, "CHF", _orderId,
                manualItemId, 400),
            ExpectedAccountRevision = 1,
            QuoteExpiresAt = DateTime.UtcNow.AddHours(1),
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
            Allocations = [new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                OrderId = _orderId,
                OrderItemId = manualItemId,
                OrderItem = manualItem,
                OrderPaymentId = manualPaymentId,
                StartOrdinal = 1,
                UnitCount = 2,
                MinorPerUnit = 400,
                AmountMinor = 800,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            }]
        });
        var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
        journal.AmountMinor = 1200;
        journal.ProviderCapturedMinor = 1200;
        await context.SaveChangesAsync();
        var source = await context.Orders.Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        source.Version.Should().BeGreaterThan(1);
        source.ServiceSession!.AccountRevision.Should().Be(1);
        source.ServiceSession.Currency.Should().Be("CHF");
        return (manualPaymentId, source.Version, source.ServiceSession.AccountRevision);
    }

    private static string ManualSnapshot(
        AccountPaymentMode mode, PaymentMethod method, long amount, string currency,
        Guid orderId, Guid itemId, long minorPerUnit)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var allocations = new[]
        {
            new
            {
                orderId,
                orderItemId = itemId,
                startOrdinal = 1,
                count = 2,
                minorPerUnit,
                totalMinor = amount
            }
        };
        return JsonSerializer.Serialize(new
        {
            expectedAccountRevision = 1,
            mode,
            paymentMethod = method,
            amountMinor = amount,
            currency,
            quoteExpiresAt = DateTime.UtcNow.AddHours(1),
            equalSharePlanId = (Guid?)null,
            equalShareOrdinal = (int?)null,
            allocations
        }, options);
    }

    private async Task RecordCanonicalCheckoutRefundAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
        var evidence = await new FakeAmendmentCheckoutEvidenceReader(_refundState)
            .ReadAsync(journal, false, CancellationToken.None);
        var updated = await new AccountCheckoutEvidenceWriter(context,
                Options.Create(new AccountCheckoutSettings()), TimeProvider.System)
            .RecordAsync(journal, evidence, CancellationToken.None);
        updated.ProviderRefundedMinor.Should().Be(600);
        updated.ReconciliationRequired.Should().BeFalse();
    }

    private async Task AssertMixedOperationStillHeld(Guid operationId, Guid manualPaymentId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var operation = await context.OrderAmendmentResolutionOperations.Include(value => value.Legs)
            .SingleAsync(value => value.Id == operationId);
        operation.State.Should().Be(OrderAmendmentResolutionOperationState.Processing);
        operation.Legs.Single(value => value.SourcePaymentId == manualPaymentId).State
            .Should().Be(OrderAmendmentRefundLegState.Pending);
        (await context.OrderBillingCredits.AnyAsync(value => value.AmendmentId == _amendmentId))
            .Should().BeFalse();
        (await context.AccountPaymentAllocationReversals.AnyAsync(value => value.OrderId == _orderId))
            .Should().BeFalse();
        var order = await context.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == _orderId);
        order.TotalPaid.Should().Be(20m);
        order.Payments.Should().OnlyContain(value => value.RefundedAmount == null);
    }
}
