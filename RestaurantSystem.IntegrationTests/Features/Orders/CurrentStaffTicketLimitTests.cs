using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CurrentStaffTicketLimitTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const int MemberBudget = 3;
    private const int AmendmentBudget = 4;

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<CurrentStaffTicketSettings>(settings =>
        {
            settings.MaximumMembers = MemberBudget;
            settings.MaximumAmendments = AmendmentBudget;
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Member_budget_keeps_the_whole_ticket_or_refuses_it(int excess)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = MemberBudget + excess;
        var orders = Enumerable.Range(0, count).Select(Order).ToArray();
        foreach (var member in orders.Skip(1)) member.AmendmentRootOrderId = orders[0].Id;
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();

        var response = await Client.GetAsync($"/api/orders/{orders[0].Id}/current-ticket");
        if (excess > 0)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("too large");
            return;
        }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadResponseAsync<ApiResponse<List<OrderDto>>>(response);
        payload!.Data!.Select(order => order.Id).Should().BeEquivalentTo(orders.Select(order => order.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Amendment_budget_preserves_the_last_change_or_refuses_the_ticket(int excess)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = Order(0);
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Ticket item",
            Quantity = 1,
            UnitPrice = 1m,
            ItemTotal = 1m,
            CreatedBy = nameof(CurrentStaffTicketLimitTests)
        };
        order.Items.Add(item);
        context.Orders.Add(order);
        var before = new OrderItemDto { Id = item.Id, ProductName = item.ProductName, Quantity = 1, ItemTotal = 1m };
        OrderAmendmentChangeSnapshot[] changes =
        [new(item.Id, OrderAmendmentChangeKind.InstructionChange, 0, 0, true, before,
            before with { SpecialInstructions = "Last committed instruction" })];
        var count = AmendmentBudget + excess;
        context.OrderAmendments.AddRange(Enumerable.Range(0, count).Select(index => new OrderAmendment
        {
            SourceOrderId = order.Id,
            State = OrderAmendmentState.Committed,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "Cashier",
            PayloadHash = new string('A', 64),
            RequestJson = "{}",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            ChangesJson = index == count - 1 ? OrderAmendmentJson.Serialize(changes) : "[]",
            CommittedAt = order.OrderDate.AddSeconds(index),
            ExpiresAt = order.OrderDate.AddHours(1),
            CreatedBy = nameof(CurrentStaffTicketLimitTests)
        }));
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();

        var response = await Client.GetAsync($"/api/orders/{order.Id}/current-ticket");
        if (excess > 0)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("too large");
            return;
        }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadResponseAsync<ApiResponse<List<OrderDto>>>(response);
        payload!.Data!.Single().Items.Single().SpecialInstructions.Should().Be("Last committed instruction");
    }

    private static Order Order(int index) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"CURRENT-LIMIT-{index}",
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 1m,
        Total = 1m,
        RemainingAmount = 1m,
        OrderDate = DateTime.UtcNow.AddSeconds(index),
        CreatedBy = nameof(CurrentStaffTicketLimitTests)
    };
}
