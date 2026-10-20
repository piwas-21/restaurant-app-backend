using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationNotifierTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Fact]
    public async Task Operational_event_maps_the_committed_order_with_its_lines_tenders_and_status_history()
    {
        var request = await AwaitingDelayAsync();
        await CancelAsync(request);
        await using var context = DatabaseFixture.CreateContext();
        var mapping = new Mock<IOrderMappingService>(MockBehavior.Strict);
        var events = new Mock<IOrderEventService>(MockBehavior.Strict);
        var dto = new OrderDto { Id = request.Intent.OrderId, Status = OrderStatus.Cancelled.ToString() };
        mapping.Setup(value => value.MapToOrderDtoAsync(It.Is<Order>(order => order.Id == request.Intent.OrderId
            && order.Status == OrderStatus.Cancelled && order.Items.Count > 0 && order.Payments.Count == 1
            && order.StatusHistory.Any(history => history.ToStatus == OrderStatus.Cancelled)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        events.Setup(value => value.NotifyOrderStatusChanged(dto, OrderStatus.PendingApproval.ToString())).Returns(Task.CompletedTask);
        var notifier = new CustomerOrderCancellationNotifier(context, mapping.Object, events.Object);
        await notifier.NotifyAsync(request.Intent.OrderId, default);
        mapping.VerifyAll();
        events.VerifyAll();
    }

    [Fact]
    public async Task A_removed_order_emits_no_operational_event_or_private_mapping()
    {
        var request = await AwaitingDelayAsync();
        await ChangeAsync(request.Intent.OrderId, (_, order) => { order.IsDeleted = true; return Task.CompletedTask; });
        await using var context = DatabaseFixture.CreateContext();
        var mapping = new Mock<IOrderMappingService>(MockBehavior.Strict);
        var events = new Mock<IOrderEventService>(MockBehavior.Strict);
        await new CustomerOrderCancellationNotifier(context, mapping.Object, events.Object).NotifyAsync(request.Intent.OrderId, default);
        mapping.VerifyNoOtherCalls();
        events.VerifyNoOtherCalls();
    }
}
