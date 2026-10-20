using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CurrentStaffTicketTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Staff_reads_full_group_with_current_instructions_and_no_persisted_item_changes()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = Order("CURRENT-ROOT");
        var supplement = Order("CURRENT-EXTRA");
        supplement.AmendmentRootOrderId = root.Id;
        var unrelated = Order("CURRENT-OTHER");
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = root.Id,
            ProductName = "Kebab",
            Quantity = 2,
            UnitPrice = 13m,
            ItemTotal = 26m,
            SpecialInstructions = "Original",
            CreatedBy = nameof(CurrentStaffTicketTests)
        };
        root.Items.Add(item);
        var removed = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = root.Id,
            ProductName = "Drink",
            Quantity = 2,
            UnitPrice = 1.5m,
            ItemTotal = 3m,
            CreatedBy = nameof(CurrentStaffTicketTests)
        };
        root.Items.Add(removed);
        context.Orders.AddRange(root, supplement, unrelated);
        var before = new OrderItemDto { Id = item.Id, Quantity = 2, ItemTotal = 26m, ProductName = "Kebab" };
        OrderAmendmentChangeSnapshot[] changes =
        [new(item.Id, OrderAmendmentChangeKind.InstructionChange, 0, 0, true, before,
            before with { SpecialInstructions = "No onion" }),
         new(removed.Id, OrderAmendmentChangeKind.Void, 2, 1, false,
             new OrderItemDto { Id = removed.Id, Quantity = 2, ItemTotal = 3m }, null)];
        context.OrderAmendments.Add(new OrderAmendment
        {
            SourceOrderId = root.Id,
            SupplementOrderId = supplement.Id,
            State = OrderAmendmentState.Committed,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "Cashier",
            PayloadHash = new string('A', 64),
            RequestJson = "{}",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(changes),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedBy = nameof(CurrentStaffTicketTests)
        });
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();
        foreach (var selected in new[] { root.Id, supplement.Id })
        {
            var response = await Client.GetAsync($"/api/orders/{selected}/current-ticket");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await ReadResponseAsync<ApiResponse<List<OrderDto>>>(response);
            payload!.Data!.Select(order => order.Id).Should().BeEquivalentTo(new[] { root.Id, supplement.Id });
            var current = payload.Data!.Single(order => order.Id == root.Id).Items.Single(line => line.Id == item.Id);
            current.Quantity.Should().Be(2);
            current.ItemTotal.Should().Be(26m);
            payload.Data!.Single(order => order.Id == root.Id).Items.Single(line => line.Id == removed.Id)
                .Quantity.Should().Be(1);
            current.SpecialInstructions.Should().Be("No onion");
        }
        context.ChangeTracker.Clear();
        var persisted = await context.OrderItems.SingleAsync(value => value.Id == item.Id);
        persisted.Quantity.Should().Be(2);
        persisted.SpecialInstructions.Should().Be("Original");
    }

    [Fact]
    public async Task Anonymous_and_customer_callers_cannot_read_current_staff_tickets()
    {
        var path = $"/api/orders/{Guid.NewGuid()}/current-ticket";
        AuthenticateAsAnonymous();
        (await Client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AuthenticateAsUser();
        (await Client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_visit_ticket_includes_all_rounds_but_excludes_other_visits_on_the_same_table()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 42,
            Currency = "EUR",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = nameof(CurrentStaffTicketTests)
        };
        var previous = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 42,
            Currency = "EUR",
            Status = TableServiceSessionStatus.Closed,
            OpenedAt = DateTime.UtcNow.AddDays(-1),
            ClosedAt = DateTime.UtcNow.AddHours(-1),
            CreatedBy = nameof(CurrentStaffTicketTests)
        };
        var first = Order("CURRENT-VISIT-1");
        var second = Order("CURRENT-VISIT-2");
        var old = Order("CURRENT-VISIT-OLD");
        first.ServiceSessionId = visit.Id;
        second.ServiceSessionId = visit.Id;
        old.ServiceSessionId = previous.Id;
        context.TableServiceSessions.AddRange(visit, previous);
        context.Orders.AddRange(first, second, old);
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/orders/{second.Id}/current-ticket");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadResponseAsync<ApiResponse<List<OrderDto>>>(response);
        payload!.Data!.Select(order => order.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
    }

    private static Order Order(string number) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = number,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 26m,
        Total = 26m,
        RemainingAmount = 26m,
        OrderDate = DateTime.UtcNow,
        CreatedBy = nameof(CurrentStaffTicketTests)
    };
}
