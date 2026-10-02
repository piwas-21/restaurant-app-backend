using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class KitchenChangeFeedTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Cancelled_order_keeps_its_frozen_correction_without_reprinting_previous_dishes()
    {
        var amendmentId = Guid.NewGuid();
        Guid jobId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IOrderKitchenChangeWriter>();
            var order = NewOrder();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var removed = new OrderItemDto
            {
                Id = Guid.NewGuid(),
                ProductName = "Cancelled soup",
                Quantity = 2,
                SpecialInstructions = "Without cream",
                KitchenType = nameof(KitchenType.BackKitchen)
            };
            jobId = await writer.StageAsync(order, amendmentId, null, DevicePrintTarget.BackKitchen,
                [new PrinterFeedChangeDto { Kind = KitchenChangeKind.Void, Previous = removed }],
                "CANCEL 2 soup", CancellationToken.None);
            order.Status = OrderStatus.Cancelled;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            removed.ProductName = "Mutated after commit";
        }

        AuthenticateAsDevice();
        var response = await Client.GetAsync("/api/orders/printer-feed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var job = feed["data"]!["updates"]!.AsArray().Single()!;
        job["jobId"]!.GetValue<Guid>().Should().Be(jobId);
        job["amendmentId"]!.GetValue<Guid>().Should().Be(amendmentId);
        job["target"]!.GetValue<string>().Should().Be("BackKitchen");
        var change = job["changes"]!.AsArray().Single()!;
        change["kind"]!.GetValue<string>().Should().Be("Void");
        change["previous"]!["productName"]!.GetValue<string>().Should().Be("Cancelled soup");
        change["previous"]!["quantity"]!.GetValue<int>().Should().Be(2);
        change["previous"]!["specialInstructions"]!.GetValue<string>().Should().Be("Without cream");
        job.ToJsonString().Should().NotContain("Earlier dish", "only the correction belongs in this job");
    }

    [Fact]
    public async Task Saved_job_is_idempotent_across_contexts_and_rejects_identity_payload_reuse()
    {
        var amendmentId = Guid.NewGuid();
        var added = new OrderItemDto { Id = Guid.NewGuid(), ProductName = "Soup", Quantity = 1 };
        var changes = new[] { new PrinterFeedChangeDto { Kind = KitchenChangeKind.Add, Current = added } };
        Guid orderId;
        Guid jobId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IOrderKitchenChangeWriter>();
            var order = NewOrder();
            orderId = order.Id;
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            jobId = await writer.StageAsync(order, amendmentId, null, DevicePrintTarget.General,
                changes, "ADD 1 soup", CancellationToken.None);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        using var replayScope = Factory.Services.CreateScope();
        var replayContext = replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var replayWriter = replayScope.ServiceProvider.GetRequiredService<IOrderKitchenChangeWriter>();
        var replayOrder = await replayContext.Orders.SingleAsync(order => order.Id == orderId);
        await using var replayTransaction = await replayContext.Database.BeginTransactionAsync();
        var replayId = await replayWriter.StageAsync(replayOrder, amendmentId, null, DevicePrintTarget.General,
            changes, "ADD 1 soup", CancellationToken.None);
        replayId.Should().Be(jobId);
        await replayContext.SaveChangesAsync();
        (await replayContext.OrderOperationalNotes.CountAsync()).Should().Be(1);

        added.Quantity = 2;
        var reuse = () => replayWriter.StageAsync(replayOrder, amendmentId, null, DevicePrintTarget.General,
            changes, "ADD 1 soup", CancellationToken.None);
        await reuse.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Rolled_back_amendment_leaves_no_print_job()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IOrderKitchenChangeWriter>();
        var order = NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await writer.StageAsync(order, Guid.NewGuid(), null, DevicePrintTarget.General,
            [new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Void,
                Previous = new OrderItemDto { Id = Guid.NewGuid(), ProductName = "Soup", Quantity = 1 }
            }], "CANCEL 1 soup", CancellationToken.None);
        await context.SaveChangesAsync();
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        (await context.OrderOperationalNotes.AnyAsync()).Should().BeFalse();
    }

    private static Order NewOrder() => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = Guid.NewGuid().ToString("N")[..16],
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        IsKitchenReleased = true,
        OrderDate = DateTime.UtcNow,
        SubTotal = 10m,
        Total = 10m,
        RemainingAmount = 10m,
        CreatedBy = "test",
        Items = [new OrderItem
        {
            Id = Guid.NewGuid(), ProductName = "Earlier dish", Quantity = 1,
            UnitPrice = 10m, ItemTotal = 10m, CreatedBy = "test"
        }]
    };
}
