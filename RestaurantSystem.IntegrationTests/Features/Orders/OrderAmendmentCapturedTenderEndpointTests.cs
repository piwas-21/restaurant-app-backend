using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Complete_attempt_proof_preserves_a_valid_contribution_spanning_two_orders()
    {
        var mixed = await PrepareMixedTenderAsync(sameUnit: true);
        var otherOrderId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        var otherPaymentId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var manual = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .SingleAsync(value => value.Allocations.Any(allocation => allocation.OrderPaymentId == mixed.PaymentId));
            var other = new Order
            {
                Id = otherOrderId,
                OrderNumber = $"CAP-{otherOrderId:N}"[..15],
                ServiceSessionId = _sessionId,
                Type = OrderType.DineIn,
                Status = OrderStatus.Completed,
                PaymentStatus = PaymentStatus.Completed,
                Total = 2m,
                SubTotal = 2m,
                TotalPaid = 2m,
                Version = 1,
                OrderDate = DateTime.UtcNow,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
                Items = [new OrderItem
                {
                    Id = otherItemId, Quantity = 1, UnitPrice = 2m, ItemTotal = 2m,
                    ProductName = "Other frozen meal", CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
                }],
                Payments = [new OrderPayment
                {
                    Id = otherPaymentId, Amount = 2m, Currency = "CHF", PaymentMethod = PaymentMethod.Cash,
                    Status = PaymentStatus.Completed, PaymentDate = DateTime.UtcNow,
                    CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
                }]
            };
            context.Orders.Add(other);
            manual.AmountMinor = 1000;
            context.AccountPaymentAllocations.Add(new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                AttemptId = manual.Id,
                OrderId = otherOrderId,
                OrderItemId = otherItemId,
                OrderPaymentId = otherPaymentId,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = 200,
                AmountMinor = 200,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            });
            var snapshot = JsonNode.Parse(manual.SnapshotJson)!;
            snapshot["amountMinor"] = 1000;
            var otherScope = snapshot["allocations"]!.AsArray()[0]!.DeepClone();
            otherScope["orderId"] = otherOrderId;
            otherScope["orderItemId"] = otherItemId;
            otherScope["count"] = 1;
            otherScope["minorPerUnit"] = 200;
            otherScope["totalMinor"] = 200;
            snapshot["allocations"]!.AsArray().Add(otherScope);
            manual.SnapshotJson = snapshot.ToJsonString();
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        using var response = await Client.PostAsJsonAsync(
            $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/quote",
            new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = _clientOperationId,
                ExpectedOrderVersion = mixed.OrderVersion,
                ExpectedAccountRevision = mixed.AccountRevision,
                Currency = "CHF"
            }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "response was {0}", await response.Content.ReadAsStringAsync());
        var quote = (await response.Content.ReadFromJsonAsync<ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.RefundMinor.Should().Be(1000);
        quote.RefundLegs.Should().HaveCount(2);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == _paymentId && value.AmountMinor == 600);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == mixed.PaymentId && value.AmountMinor == 400);
        quote.RefundLegs.SelectMany(value => value.Scopes).Should().OnlyContain(value => value.OrderItemId == _itemId);
        _refundState.CreateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(800)]
    public async Task Duplicate_distinct_scopes_cannot_refund_more_than_the_original_attempt_or_tender(long attemptMinor)
    {
        var mixed = await PrepareMixedTenderAsync(sameUnit: true);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var provider = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .SingleAsync(value => value.Id == _attemptId);
            provider.AmountMinor = 400;
            provider.Allocations.Single().MinorPerUnit = 200;
            provider.Allocations.Single().AmountMinor = 400;
            provider.SnapshotJson = ManualSnapshot(AccountPaymentMode.Amount,
                PaymentMethod.OnlinePayment, 400, "CHF", _orderId, _itemId, 200);
            (await context.OrderPayments.SingleAsync(value => value.Id == _paymentId)).Amount = 4m;
            var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.AmountMinor = 400;
            journal.ProviderCapturedMinor = 400;

            var manual = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .SingleAsync(value => value.Allocations.Any(allocation => allocation.OrderPaymentId == mixed.PaymentId));
            manual.AmountMinor = attemptMinor;
            var first = manual.Allocations.Single();
            first.UnitCount = 1;
            first.MinorPerUnit = 400;
            first.AmountMinor = 400;
            var duplicate = new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                AttemptId = manual.Id,
                OrderId = _orderId,
                OrderItemId = _itemId,
                OrderPaymentId = mixed.PaymentId,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = 400,
                AmountMinor = 400,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            };
            context.AccountPaymentAllocations.Add(duplicate);
            var snapshot = JsonNode.Parse(manual.SnapshotJson)!;
            snapshot["amountMinor"] = attemptMinor;
            var scopes = snapshot["allocations"]!.AsArray();
            scopes[0]!["count"] = 1;
            scopes[0]!["totalMinor"] = 400;
            scopes.Add(scopes[0]!.DeepClone());
            manual.SnapshotJson = snapshot.ToJsonString();
            (await context.OrderPayments.SingleAsync(value => value.Id == mixed.PaymentId)).Amount = 4m;
            var order = await context.Orders.SingleAsync(value => value.Id == _orderId);
            order.TotalPaid = 8m;
            order.RemainingAmount = 12m;
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
            await context.SaveChangesAsync();
        }

        await using var proof = DatabaseFixture.CreateContext();
        var source = await proof.Orders.Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        AuthenticateAsAdmin();
        using var response = await Client.PostAsJsonAsync(
            $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/quote",
            new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = _clientOperationId,
                ExpectedOrderVersion = source.Version,
                ExpectedAccountRevision = source.ServiceSession!.AccountRevision,
                Currency = "CHF"
            }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(attemptMinor == 400
            ? "captured or reserved totals require reconciliation"
            : "no matching tender evidence");
        _refundState.CreateCalls.Should().Be(0);
        (await proof.OrderAmendmentResolutionOperations.CountAsync()).Should().Be(0);
        (await proof.OrderBillingCredits.CountAsync()).Should().Be(0);
        (await proof.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
    }
}
