using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CashierCurrentTicketTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_bill_includes_all_current_rounds_from_either_selected_order(bool selectSupplement)
    {
        await using var context = DatabaseFixture.CreateContext();
        var root = Order("CASHIER-ROOT", 10);
        var extra = Order("CASHIER-EXTRA", 8);
        extra.AmendmentRootOrderId = root.Id;
        var unrelated = Order("CASHIER-OTHER", 99);
        context.Orders.AddRange(root, extra, unrelated);
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();
        var selected = selectSupplement ? extra : root;
        var operationId = Guid.NewGuid();
        var request = new { operationId, orderId = selected.Id, expectedVersion = selected.Version };
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => Client.PostAsJsonAsync("/api/cashier-print-jobs", request)));
        foreach (var response in concurrent) response.EnsureSuccessStatusCode();
        var job = await context.CashierPrintJobs.AsNoTracking().SingleAsync();
        var bill = JsonSerializer.Deserialize<OrderDto>(job.SnapshotJson, JsonOptions)!;
        bill.Items.Select(item => item.ProductName).Should().BeEquivalentTo(new[] { "CASHIER-ROOT", "CASHIER-EXTRA" });
        bill.SubTotal.Should().Be(18);
        bill.PayableTotal.Should().Be(18);
        bill.RemainingAmount.Should().Be(18);
        bill.Items.Should().OnlyContain(item => item.SpecialInstructions == null);
        bill.Id.Should().Be(selected.Id);
        root.Items.Single().SpecialInstructions.Should().Be("Kitchen only");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_bill_applies_committed_removals_and_current_choices_for_order_and_visit(bool visit)
    {
        await using var context = DatabaseFixture.CreateContext();
        var root = Order("CASHIER-EDITED", 20);
        var item = root.Items.Single();
        item.Quantity = 2;
        item.UnitPrice = 10;
        root.SubTotal = 25;
        root.Total = 25;
        root.BillingCreditAmount = 10;
        root.RemainingAmount = 15;
        var drink = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = root.Id,
            ProductName = "Drink",
            Quantity = 1,
            UnitPrice = 5,
            ItemTotal = 5,
            CreatedBy = nameof(CashierCurrentTicketTests)
        };
        root.Items.Add(drink);
        var current = new OrderItemDto
        {
            Id = drink.Id,
            ProductName = drink.ProductName,
            Quantity = 1,
            UnitPrice = 5,
            ItemTotal = 5,
            SpecialInstructions = "Still kitchen only",
            IngredientCustomizations = [new() { IngredientName = "Onion", IsRemoved = true },
                new() { IngredientName = "Garlic sauce", IsAddOn = true, Quantity = 1 }]
        };
        context.OrderAmendments.Add(new OrderAmendment
        {
            SourceOrderId = root.Id,
            State = OrderAmendmentState.Committed,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "Cashier",
            PayloadHash = new string('A', 64),
            RequestJson = "{}",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
                "CHF", 0, 1000, -1000, 1000, OrderAmendmentFinancialResolutionStatus.NotRequired,
                OrderAmendmentCreditState.None, OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None)),
            ChangesJson = OrderAmendmentJson.Serialize(new OrderAmendmentChangeSnapshot[]
            {
                new(drink.Id, OrderAmendmentChangeKind.InstructionChange, 0, 0, true, current, current),
                new(item.Id, OrderAmendmentChangeKind.Void, 2, 1, false,
                    new OrderItemDto { Id = item.Id, ProductName = item.ProductName, Quantity = 1, ItemTotal = 10 }, null)
            }),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedBy = nameof(CashierCurrentTicketTests)
        });
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 42,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = nameof(CashierCurrentTicketTests)
        };
        if (visit)
        {
            root.Type = OrderType.DineIn;
            root.ServiceSessionId = session.Id;
            context.TableServiceSessions.Add(session);
        }
        context.Orders.Add(root);
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();
        object request = visit
            ? new { operationId = Guid.NewGuid(), serviceSessionId = session.Id, expectedVersion = session.Version }
            : new { operationId = Guid.NewGuid(), orderId = root.Id, expectedVersion = root.Version };
        var response = await Client.PostAsJsonAsync("/api/cashier-print-jobs", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var job = await context.CashierPrintJobs.AsNoTracking().SingleAsync();
        var bill = JsonSerializer.Deserialize<OrderDto>(job.SnapshotJson, JsonOptions)!;
        bill.Items.Should().HaveCount(2);
        var line = bill.Items.Single(value => value.Id == item.Id);
        line.Quantity.Should().Be(1);
        line.ItemTotal.Should().Be(10);
        line.SpecialInstructions.Should().BeNull();
        bill.Items.Single(value => value.Id == drink.Id).IngredientCustomizations!.Select(value => value.IngredientName)
            .Should().BeEquivalentTo(new[] { "Onion", "Garlic sauce" });
        bill.SubTotal.Should().Be(15);
        bill.PayableTotal.Should().Be(15);
        bill.BillingCreditAmount.Should().Be(0, "the current subtotal already excludes the voided unit");
        context.ChangeTracker.Clear();
        (await context.OrderItems.SingleAsync(value => value.Id == item.Id)).Quantity.Should().Be(2);
    }

    private static Order Order(string number, decimal amount)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = amount,
            Total = amount,
            RemainingAmount = amount,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(CashierCurrentTicketTests)
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = number,
            Quantity = 1,
            UnitPrice = amount,
            ItemTotal = amount,
            SpecialInstructions = "Kitchen only",
            CreatedBy = nameof(CashierCurrentTicketTests)
        });
        return order;
    }
}
