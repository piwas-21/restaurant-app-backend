using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAcceptanceTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    [Theory]
    [InlineData(OrderType.Takeaway, PaymentMethod.Cash)]
    [InlineData(OrderType.Takeaway, PaymentMethod.CreditCard)]
    [InlineData(OrderType.Delivery, PaymentMethod.Cash)]
    public async Task One_acceptance_saves_server_money_uncaptured_tender_snapshot_and_clears_owned_basket(
        OrderType type, PaymentMethod method)
    {
        var intent = await IntentAsync(type, method);
        var submission = await PreparedAsync(intent);
        await AssertNoFinancialEffectsAsync();
        var resolution = await OrderResolutionAsync(await SubmitAsync(submission));
        resolution.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        resolution.OperationId.Should().Be(submission.OperationId);
        await using var read = DatabaseFixture.CreateContext();
        var order = await read.Orders.Include(value => value.Payments).Include(value => value.DeliveryAddress).SingleAsync();
        order.Id.Should().Be(resolution.OrderId!.Value);
        order.SubTotal.Should().Be(14m);
        order.Tip.Should().Be(3m);
        order.Total.Should().Be(14m + order.DeliveryFee + 3m);
        order.TotalPaid.Should().Be(0m);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        order.Payments.Should().ContainSingle().Which.Status.Should().Be(PaymentStatus.Pending);
        order.Payments.Single().PaymentMethod.Should().Be(method);
        order.Payments.Single().Amount.Should().Be(order.Total);
        order.CustomerName.Should().Be(intent.Contact.Name);
        order.CustomerEmail.Should().Be(intent.Contact.Email);
        order.QuickActionToken.Should().NotBe(order.GuestStatusToken);
        var snapshot = await read.Set<OrderBillingSnapshot>().SingleAsync(value => value.OrderId == order.Id);
        snapshot.TotalMinor.Should().Be(decimal.ToInt64(order.Total * 100));
        snapshot.Currency.Should().Be("CHF");
        (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
        (await ReadAsync()).Items.Should().BeEmpty();
        if (type == OrderType.Delivery)
        {
            order.DeliveryAddress!.AddressLine1.Should().Be("Fixture street");
            order.DeliveryAddress.Country.Should().BeEmpty(); // Optional field does not invent a country.
        }
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Replay_recovery_and_cancel_return_original_acceptance_after_channel_withdrawal_without_effects()
    {
        var submission = await PreparedAsync(await IntentAsync());
        var first = await OrderResolutionAsync(await SubmitAsync(submission));
        await EnableAsync(OrderType.Takeaway, false);
        (await OrderResolutionAsync(await SubmitAsync(submission))).Should().Be(first);
        (await OrderResolutionAsync(await ResolveAsync(submission))).Should().Be(first);
        (await OrderResolutionAsync(await ResolveAsync(submission, cancel: true))).Should().Be(first);
        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.CountAsync()).Should().Be(1);
        (await read.OrderPayments.CountAsync()).Should().Be(1);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unseen_recovery_writes_nothing_but_cancellation_seals_late_submission()
    {
        var submission = await PreparedAsync(await IntentAsync());
        (await OrderResolutionAsync(await ResolveAsync(submission))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        await using (var read = DatabaseFixture.CreateContext())
            (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        var cancelled = await OrderResolutionAsync(await ResolveAsync(submission, cancel: true));
        cancelled.Outcome.Should().Be(CustomerOrderOperationOutcome.Cancelled);
        cancelled.OrderId.Should().BeNull();
        (await SubmitAsync(submission)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OrderResolutionAsync(await ResolveAsync(submission))).Should().Be(cancelled);
        await AssertNoFinancialEffectsAsync();
        (await ReadAsync()).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task A_post_commit_notification_failure_still_returns_and_recovers_one_accepted_order()
    {
        Notifications.Setup(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Fixture notification delivery unavailable"));
        var submission = await PreparedAsync(await IntentAsync());
        var first = await OrderResolutionAsync(await SubmitAsync(submission));
        first.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        (await OrderResolutionAsync(await SubmitAsync(submission))).Should().Be(first);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
