using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationTenderTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Theory]
    [InlineData("captured")]
    [InlineData("processing")]
    [InlineData("online")]
    [InlineData("zero-amount")]
    [InlineData("tip")]
    [InlineData("refund-flag")]
    [InlineData("refund-zero")]
    [InlineData("refund-date")]
    [InlineData("refund-reason")]
    [InlineData("refund-tip")]
    [InlineData("transaction")]
    [InlineData("currency")]
    [InlineData("reference")]
    [InlineData("card-number")]
    [InlineData("card-type")]
    [InlineData("gateway")]
    [InlineData("operation")]
    [InlineData("notes")]
    [InlineData("empty-notes")]
    public async Task A_single_tender_with_any_collection_provider_or_refund_evidence_requires_restaurant_review(string evidence)
    {
        var request = await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, async (context, order) =>
        {
            var tender = await context.OrderPayments.SingleAsync(value => value.OrderId == order.Id);
            switch (evidence)
            {
                case "captured": tender.Status = PaymentStatus.Completed; break;
                case "processing": tender.Status = PaymentStatus.Processing; break;
                case "online": tender.PaymentMethod = PaymentMethod.OnlinePayment; break;
                case "zero-amount": tender.Amount = 0; break;
                case "tip": tender.TipMinor = 1; break;
                case "refund-flag": tender.IsRefunded = true; break;
                case "refund-zero": tender.RefundedAmount = 0; break;
                case "refund-date": tender.RefundDate = DateTime.UtcNow; break;
                case "refund-reason": tender.RefundReason = "Unresolved"; break;
                case "refund-tip": tender.RefundedTipMinor = 1; break;
                case "transaction": tender.TransactionId = "Unresolved"; break;
                case "currency": tender.Currency = "chf"; break;
                case "reference": tender.ReferenceNumber = "Unresolved"; break;
                case "card-number": tender.CardLastFourDigits = "1234"; break;
                case "card-type": tender.CardType = "Unresolved"; break;
                case "gateway": tender.PaymentGateway = "Unresolved"; break;
                case "operation": tender.OperationId = Guid.NewGuid(); break;
                case "notes": tender.PaymentNotes = "Restaurant review"; break;
                case "empty-notes": tender.PaymentNotes = ""; break;
                default: throw new ArgumentException("Unknown tender evidence", nameof(evidence));
            }
        });
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(await SendAsync(CancellationPath + "/original/prepare", request.Proof));
        preparation.Allowed.Should().BeFalse();
        preparation.Reason.Should().Be(CustomerOrderCancellationReason.FinancialReview);
        var current = request with
        {
            Intent = request.Intent with
            {
                ExpectedVersion = preparation.ExpectedVersion,
                PayloadHash = CustomerOrderCancellationHash.Payload(request.Intent.OrderId, preparation.ExpectedVersion)
            }
        };
        var result = await CancelAsync(current);
        result.Outcome.Should().Be(CustomerOrderCancellationOutcome.Refused);
        result.Reason.Should().Be(CustomerOrderCancellationReason.FinancialReview);
        CancellationNotifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unchanged_pending_card_at_restaurant_intent_has_no_captured_money_to_reverse()
    {
        var request = await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, async (context, order) =>
        {
            (await context.OrderPayments.SingleAsync(value => value.OrderId == order.Id)).PaymentMethod = PaymentMethod.CreditCard;
        });
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(await SendAsync(CancellationPath + "/original/prepare", request.Proof));
        preparation.Allowed.Should().BeTrue();
        var current = request with
        {
            Intent = request.Intent with
            {
                ExpectedVersion = preparation.ExpectedVersion,
                PayloadHash = preparation.PayloadHash!
            }
        };
        (await CancelAsync(current)).Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tender = await context.OrderPayments.SingleAsync();
        tender.PaymentMethod.Should().Be(PaymentMethod.CreditCard);
        tender.Status.Should().Be(PaymentStatus.Pending);
        tender.IsRefunded.Should().BeFalse();
        (await context.Orders.SingleAsync()).TotalPaid.Should().Be(0);
    }
}
