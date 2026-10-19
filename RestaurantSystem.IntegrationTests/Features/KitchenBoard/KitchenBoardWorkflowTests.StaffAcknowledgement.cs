using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.KitchenBoard;

public sealed partial class KitchenBoardWorkflowTests
{
    [Theory]
    [InlineData(DevicePrintStatus.NotConfigured)]
    [InlineData(DevicePrintStatus.Unknown)]
    public async Task Staff_can_acknowledge_completed_work_without_fabricating_printer_receipts(DevicePrintStatus routeStatus)
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, routeStatus);
        var correctionId = await SeedActiveCorrectionAsync(orderId);
        AuthenticateAsRole(UserRole.Cashier);
        var version = await GetOrderVersionAsync(orderId);
        var refused = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/orders/{orderId}/status", new { newStatus = "Completed", expectedVersion = version })))!;
        refused.Success.Should().BeFalse();
        var accepted = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/orders/{orderId}/status", new
            {
                newStatus = "Completed",
                expectedVersion = version,
                acknowledgeCompletedWork = true
            })))!;
        accepted.Success.Should().BeTrue();
        accepted.Data!.Status.Should().Be("Completed");
        await using var context = DatabaseFixture.CreateContext();
        (await context.KitchenBoardWorkCompletions.Where(work => work.OrderId == orderId).ToListAsync())
            .Should().Contain(work => work.Kind == KitchenBoardWorkKind.StaffHandover)
            .And.Contain(work => work.WorkItemId == correctionId);
        (await context.DeviceOrderReceipts.CountAsync(receipt => receipt.OrderId == orderId)).Should().Be(0);
        (await context.OrderRoutingStates.SingleAsync(route => route.OrderId == orderId)).Status.Should().Be(routeStatus);
        (await context.Orders.SingleAsync(order => order.Id == orderId)).TotalPaid.Should().Be(0);
    }

    [Fact]
    public async Task Staff_acknowledgement_requires_the_exact_current_order_version()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, DevicePrintStatus.NotConfigured);
        AuthenticateAsRole(UserRole.Cashier);
        var refused = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/orders/{orderId}/status", new
            {
                newStatus = "Completed",
                expectedVersion = 99,
                acknowledgeCompletedWork = true
            })))!;
        refused.Success.Should().BeFalse();
        refused.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);
        await using var context = DatabaseFixture.CreateContext();
        (await context.KitchenBoardWorkCompletions.CountAsync(work => work.OrderId == orderId)).Should().Be(0);
    }
}
