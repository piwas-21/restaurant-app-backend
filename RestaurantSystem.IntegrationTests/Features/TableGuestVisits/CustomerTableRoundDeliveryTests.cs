using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundDeliveryTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    [Fact]
    public async Task Notification_sees_both_committed_receipts_and_failure_does_not_change_or_repeat_the_decision()
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        var observed = false;
        Notifications.Setup(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()))
            .Returns(async (Order order, bool _, CancellationToken cancellationToken) =>
            {
                await using var observer = DatabaseFixture.CreateContext();
                (await observer.Orders.AsNoTracking().SingleAsync(cancellationToken)).Id.Should().Be(order.Id);
                (await observer.Set<CustomerOrderOperation>().AsNoTracking().SingleAsync(cancellationToken)).OrderId.Should().Be(order.Id);
                (await observer.Set<TableGuestRoundOperation>().AsNoTracking().SingleAsync(cancellationToken)).OrderId.Should().Be(order.Id);
                (await observer.BasketItems.CountAsync(cancellationToken)).Should().Be(0);
                observed = true;
                throw new IOException("Injected post-commit delivery failure.");
            });
        var submitted = Submission(intent, prepared);
        var applied = await RoundResolutionAsync(await SendAsync(RoundPath + "submit", submitted));
        observed.Should().BeTrue();
        applied.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Should().Be(applied);
        (await RoundResolutionAsync(await SendAsync(RoundPath + "submit", submitted))).Should().Be(applied);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared, true))).Should().Be(applied);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()), Times.Once);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_identical_submissions_share_one_order_and_one_notification()
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        var submitted = Submission(intent, prepared);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(RoundPath + "submit", submitted)));
        var resolutions = await Task.WhenAll(responses.Select(RoundResolutionAsync));
        resolutions.Should().OnlyContain(value => value == resolutions[0]);
        resolutions[0].Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
        (await context.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
