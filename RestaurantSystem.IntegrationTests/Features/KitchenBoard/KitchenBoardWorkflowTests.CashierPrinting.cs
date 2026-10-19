using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.KitchenBoard;

public sealed partial class KitchenBoardWorkflowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Closing_a_visit_queues_one_full_bill_and_empty_visits_still_close(int rounds)
    {
        var sessionId = Guid.NewGuid();
        int version;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var session = new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 34434,
                Currency = "CHF",
                OpenedAt = TestNow,
                CreatedBy = nameof(KitchenBoardWorkflowTests)
            };
            context.TableServiceSessions.Add(session);
            for (var index = 0; index < rounds; index++)
            {
                var product = NewRecipeProduct($"Round {index}");
                context.Products.Add(product);
                var order = NewOrder(Guid.NewGuid(), OrderStatus.Completed);
                order.Type = OrderType.DineIn; order.ServiceSessionId = sessionId; order.TableNumber = 34434;
                order.TotalPaid = order.Total; order.RemainingAmount = 0; order.PaymentStatus = PaymentStatus.Completed;
                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = 1,
                    UnitPrice = 25,
                    ItemTotal = 25,
                    CreatedBy = nameof(KitchenBoardWorkflowTests)
                });
                context.Orders.Add(order);
            }
            await context.SaveChangesAsync();
            version = session.Version;
        }
        AuthenticateAsRole(UserRole.Cashier);
        var response = await Client.PostAsJsonAsync($"/api/table-service-sessions/{sessionId}/close",
            new { expectedVersion = version });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("Closed");
        (await Client.PostAsJsonAsync($"/api/table-service-sessions/{sessionId}/close", new { expectedVersion = version }))
            .EnsureSuccessStatusCode();
        await using var final = DatabaseFixture.CreateContext();
        var jobs = await final.CashierPrintJobs.Where(job => job.ServiceSessionId == sessionId).ToListAsync();
        jobs.Should().HaveCount(rounds > 0 ? 1 : 0);
        if (rounds > 0)
        {
            jobs[0].IsAutomatic.Should().BeTrue();
            var receipt = System.Text.Json.JsonSerializer.Deserialize<RestaurantSystem.Api.Features.Orders.Dtos.OrderDto>(
                jobs[0].SnapshotJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            receipt.Items.Should().HaveCount(2);
            receipt.Total.Should().Be(50);
            receipt.TotalPaid.Should().Be(50);
        }
    }

    [Fact]
    public async Task Cashier_bill_requests_are_idempotent_and_device_claims_never_replay()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, DevicePrintStatus.NotConfigured);
        AuthenticateAsRole(UserRole.Cashier);
        var operationId = Guid.NewGuid();
        var request = new { operationId, orderId, expectedVersion = await GetOrderVersionAsync(orderId) };
        var queued = (await ReadResponseAsync<ApiResponse<CashierPrintJobDto>>(
            await Client.PostAsJsonAsync("/api/cashier-print-jobs", request)))!;
        queued.Success.Should().BeTrue();
        queued.Data!.Status.Should().Be("Queued");
        var replay = (await ReadResponseAsync<ApiResponse<CashierPrintJobDto>>(
            await Client.PostAsJsonAsync("/api/cashier-print-jobs", request)))!;
        replay.Data!.JobId.Should().Be(operationId);
        await using (var context = DatabaseFixture.CreateContext())
        {
            (await context.CashierPrintJobs.CountAsync()).Should().Be(1);
            context.PrinterDeviceTargetCapabilities.Add(new PrinterDeviceTargetCapability
            {
                DeviceId = "cashier-queue-test",
                Target = DevicePrintTarget.Cashier,
                IsSupported = true,
                IsConfigured = true,
                AutoPrintEnabled = false,
                ReportedAt = DateTime.UtcNow,
                CreatedBy = nameof(KitchenBoardWorkflowTests),
            });
            await context.SaveChangesAsync();
        }
        AuthenticateAsDevice();
        Client.DefaultRequestHeaders.Add("X-Device-Id", "cashier-queue-test");
        var claim = (await ReadResponseAsync<ApiResponse<CashierPrintJobDto>>(
            await Client.PostAsync("/api/cashier-print-jobs/claim", null)))!;
        claim.Data!.JobId.Should().Be(operationId);
        claim.Data.Receipt.Should().NotBeNull();
        claim.Data.Receipt!.GuestStatusToken.Should().BeNull();
        var second = (await ReadResponseAsync<ApiResponse<CashierPrintJobDto>>(
            await Client.PostAsync("/api/cashier-print-jobs/claim", null)))!;
        second.Data.Should().BeNull("a lost claim response must not redrive output");
        (await Client.PostAsJsonAsync($"/api/cashier-print-jobs/{operationId}/complete", new { status = "Sent" }))
            .EnsureSuccessStatusCode();
        await using var final = DatabaseFixture.CreateContext();
        (await final.CashierPrintJobs.SingleAsync()).Status.Should().Be("Sent");
        (await final.DeviceOrderReceipts.CountAsync()).Should().Be(0);
    }
}
