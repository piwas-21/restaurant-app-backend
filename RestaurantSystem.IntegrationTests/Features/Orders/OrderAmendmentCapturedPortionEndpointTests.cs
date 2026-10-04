using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Overcaptured_unit_is_held_even_when_total_capture_is_below_the_whole_order_charge()
    {
        var mixed = await PrepareMixedTenderAsync(sameUnit: true);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var payment = await context.OrderPayments.SingleAsync(value => value.Id == mixed.PaymentId);
            payment.Amount = 4.01m;
            var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .SingleAsync(value => value.Allocations.Any(allocation => allocation.OrderPaymentId == mixed.PaymentId));
            attempt.AmountMinor = 401;
            var allocation = attempt.Allocations.Single();
            allocation.UnitCount = 1;
            allocation.MinorPerUnit = 401;
            allocation.AmountMinor = 401;
            var snapshot = JsonNode.Parse(attempt.SnapshotJson)
                ?? throw new Xunit.Sdk.XunitException("The fixed capture fixture has no snapshot.");
            snapshot["amountMinor"] = 401;
            var scopes = snapshot["allocations"]?.AsArray()
                ?? throw new Xunit.Sdk.XunitException("The fixed capture fixture has no allocation.");
            var scope = scopes.Single()
                ?? throw new Xunit.Sdk.XunitException("The fixed capture fixture has a null allocation.");
            scope["count"] = 1;
            scope["minorPerUnit"] = 401;
            scope["totalMinor"] = 401;
            attempt.SnapshotJson = snapshot.ToJsonString();
            var order = await context.Orders.SingleAsync(value => value.Id == _orderId);
            order.TotalPaid = 16.01m;
            order.RemainingAmount = 3.99m;
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
            await context.SaveChangesAsync();
        }

        await using var proof = DatabaseFixture.CreateContext();
        var source = await proof.Orders.Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        source.Total.Should().Be(20m);
        source.TotalPaid.Should().Be(16.01m);
        source.RemainingAmount.Should().Be(3.99m);
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
        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "Payment allocations exceed a frozen unit balance. Reconciliation is required.");
        _refundState.CreateCalls.Should().Be(0);
        (await proof.OrderAmendmentResolutionOperations.CountAsync()).Should().Be(0);
        (await proof.OrderBillingCredits.CountAsync()).Should().Be(0);
        (await proof.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
    }
}
