using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationRefusalTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Theory]
    [InlineData("paid")]
    [InlineData("payment-status")]
    [InlineData("billing-credit")]
    [InlineData("bill-charge")]
    [InlineData("charge-evidence")]
    [InlineData("redeemed-points")]
    [InlineData("earned-points")]
    [InlineData("point-discount")]
    [InlineData("zero-tender")]
    [InlineData("failed-tender")]
    [InlineData("expired-checkout")]
    [InlineData("native-journal")]
    [InlineData("zero-scalar-amendment")]
    [InlineData("zero-loyalty")]
    public async Task Financial_or_loyalty_evidence_requires_restaurant_review_even_when_zero_or_failed(string evidence)
    {
        var request = evidence is "redeemed-points" or "earned-points" or "point-discount"
            ? await HistoricalDelayAsync() : await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, async (context, order) =>
        {
            switch (evidence)
            {
                case "paid": order.TotalPaid = 1; break;
                case "payment-status": order.PaymentStatus = PaymentStatus.Completed; break;
                case "billing-credit": order.BillingCreditAmount = 1; break;
                case "bill-charge": order.BillChargeAmount = 1; break;
                case "charge-evidence": order.BillChargesJson = "[{}]"; break;
                case "redeemed-points": order.FidelityPointsRedeemed = 1; break;
                case "earned-points": order.FidelityPointsEarned = 1; break;
                case "point-discount": order.FidelityPointsDiscount = 1; break;
                case "zero-tender":
                case "failed-tender":
                    context.OrderPayments.Add(new OrderPayment
                    {
                        OrderId = order.Id,
                        Amount = 0,
                        PaymentMethod = PaymentMethod.Cash,
                        Status = evidence == "failed-tender" ? PaymentStatus.Failed : PaymentStatus.Pending,
                        PaymentDate = DateTime.UtcNow,
                        CreatedBy = nameof(CustomerOrderCancellationRefusalTests)
                    });
                    break;
                case "expired-checkout":
                    context.OrderCheckoutSessions.Add(new OrderCheckoutSession
                    {
                        OrderId = order.Id,
                        SessionId = "expired-fixture",
                        Currency = "chf",
                        AmountMinor = 0,
                        IdempotencyKey = "expired-fixture",
                        ConnectedAccountId = "account-fixture",
                        Status = CheckoutSessionStatus.Expired,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                        CreatedBy = nameof(CustomerOrderCancellationRefusalTests)
                    });
                    break;
                case "native-journal":
                    context.Set<CustomerOrderCheckoutJournal>().Add(new()
                    {
                        OrderId = order.Id,
                        OperationId = Guid.NewGuid(),
                        OrderNumber = order.OrderNumber,
                        Currency = "chf",
                        AmountMinor = 100,
                        ProviderAccountId = "fixture",
                        CreateIdempotencyKey = Guid.NewGuid().ToString("D"),
                        CreatePayloadHash = new string('a', 64),
                        ReturnUrl = "/fixture-return",
                        StartedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                        MaximumCreateRetryAt = DateTime.UtcNow.AddHours(1)
                    });
                    break;
                case "zero-scalar-amendment":
                    context.OrderAmendments.Add(new OrderAmendment
                    {
                        SourceOrderId = order.Id,
                        ExpectedOrderVersion = order.Version,
                        ActorRole = "Cashier",
                        PayloadHash = new string('a', 64),
                        RequestJson = "{}",
                        ChangesJson = "[]",
                        SourceSnapshotJson = "{}",
                        FinancialResolutionJson = "{}",
                        ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                        CreatedBy = "Fixture"
                    });
                    break;
                case "zero-loyalty":
                    context.FidelityPointsTransactions.Add(new FidelityPointsTransaction
                    {
                        OrderId = order.Id,
                        Points = 0,
                        TransactionType = TransactionType.Redeemed,
                        CreatedBy = nameof(CustomerOrderCancellationRefusalTests)
                    });
                    break;
                default: throw new ArgumentException("Unknown financial evidence", nameof(evidence));
            }
        });
        await AssertRefusedAsync(request, CustomerOrderCancellationReason.FinancialReview);
    }

    [Theory]
    [InlineData("confirmed", CustomerOrderCancellationReason.NotAwaitingCustomerApproval)]
    [InlineData("acknowledgement", CustomerOrderCancellationReason.NotAwaitingCustomerApproval)]
    [InlineData("disabled", CustomerOrderCancellationReason.OrderingUnavailable)]
    [InlineData("staff-work", CustomerOrderCancellationReason.RestaurantManaged)]
    [InlineData("table", CustomerOrderCancellationReason.RestaurantManaged)]
    [InlineData("external", CustomerOrderCancellationReason.RestaurantManaged)]
    public async Task Current_state_configuration_and_origin_are_rechecked_before_submission(string state, CustomerOrderCancellationReason reason)
    {
        var request = await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, async (context, order) =>
        {
            switch (state)
            {
                case "confirmed": order.Status = OrderStatus.Confirmed; break;
                case "acknowledgement":
                    (await context.OrderTypeConfigurations.SingleAsync(row => row.OrderType == order.Type))
                        .ConfirmationFlow = OrderConfirmationFlows.Acknowledge;
                    break;
                case "disabled":
                    (await context.OrderTypeConfigurations.SingleAsync(row => row.OrderType == order.Type)).IsEnabled = false;
                    break;
                case "staff-work":
                    context.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        FromStatus = OrderStatus.Confirmed,
                        ToStatus = OrderStatus.PendingApproval,
                        ChangedAt = DateTime.UtcNow,
                        ChangedBy = "Staff",
                        CreatedBy = "Staff"
                    });
                    break;
                case "table": order.Type = OrderType.DineIn; order.TableNumber = 1; break;
                case "external":
                    context.Set<ExternalOrderReference>().Add(new ExternalOrderReference
                    {
                        OrderId = order.Id,
                        Provider = "fixture",
                        ExternalStoreId = "fixture",
                        ExternalOrderId = "fixture",
                        Currency = "CHF",
                        PayloadHash = new string('a', 64),
                        CreatedBy = "Fixture"
                    });
                    break;
                default: throw new ArgumentException("Unknown state", nameof(state));
            }
        });
        await AssertRefusedAsync(request, reason);
    }

    private async Task AssertRefusedAsync(CustomerOrderCancellationGuestDto original, CustomerOrderCancellationReason reason)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await context.Orders.AsNoTracking().SingleAsync(row => row.Id == original.Intent.OrderId);
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(await SendAsync(CancellationPath + "/original/prepare", original.Proof));
        preparation.Allowed.Should().BeFalse();
        preparation.Reason.Should().Be(reason);
        preparation.PayloadHash.Should().BeNull();
        // A hostile client can compute the public intent digest, so submit must recheck clearance.
        var request = original with
        {
            Intent = original.Intent with
            {
                ExpectedVersion = before.Version,
                PayloadHash = CustomerOrderCancellationHash.Payload(before.Id, before.Version)
            }
        };
        var result = await CancelAsync(request);
        result.Outcome.Should().Be(CustomerOrderCancellationOutcome.Refused);
        result.Reason.Should().Be(reason);
        result.ObservedVersion.Should().Be(before.Version);
        (await CancelAsync(request)).Should().BeEquivalentTo(result);
        (await context.Orders.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Should().BeEquivalentTo(before, options => options.IgnoringCyclicReferences());
        (await context.OrderStatusHistories.AnyAsync(row => row.ToStatus == OrderStatus.Cancelled)).Should().BeFalse();
        (await DecisionsAsync()).Should().ContainSingle();
        CancellationNotifications.VerifyNoOtherCalls();
    }
}
