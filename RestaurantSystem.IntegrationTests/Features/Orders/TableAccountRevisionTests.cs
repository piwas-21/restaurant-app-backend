using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class TableAccountRevisionTests : IntegrationTestBase
{
    public TableAccountRevisionTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Cancelling_a_member_round_advances_account_revision_once()
    {
        AuthenticateAsRole(UserRole.Admin);
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 73,
                Status = TableServiceSessionStatus.Open,
                Version = 3,
                AccountRevision = 4,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = nameof(TableAccountRevisionTests)
            });
            context.Orders.Add(new Order
            {
                Id = orderId,
                OrderNumber = $"CN-{orderId:N}"[..16],
                Type = OrderType.DineIn,
                TableNumber = 73,
                ServiceSessionId = sessionId,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                SubTotal = 10m,
                Total = 10m,
                RemainingAmount = 10m,
                OrderDate = DateTime.UtcNow,
                CreatedBy = nameof(TableAccountRevisionTests)
            });
            await context.SaveChangesAsync();
        }
        var response = await Client.PostAsJsonAsync($"/api/orders/{orderId}/cancel",
            new { cancellationReason = "Guests changed their request" });
        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeTrue();
        var after = await ReadSessionAsync(sessionId);
        after.AccountRevision.Should().Be(5);
        after.Version.Should().Be(4);
        after.Bill.AccountItems.Should().BeEmpty();
        after.Bill.AccountActivity.Should().Contain(entry => entry.OrderId == orderId
            && entry.Kind == "StatusChanged" && entry.Status == nameof(OrderStatus.Cancelled));

        var replay = await Client.PostAsJsonAsync($"/api/orders/{orderId}/cancel",
            new { cancellationReason = "Guests changed their request" });
        (await ReadResponseAsync<ApiResponse<OrderDto>>(replay))!.Success.Should().BeFalse();
        (await ReadSessionAsync(sessionId)).AccountRevision.Should().Be(5);
    }

    [Fact]
    public void Account_projection_preserves_large_quantity_without_expanding_units()
    {
        var itemId = Guid.NewGuid();
        var result = TableAccountItemProjection.Project([new OrderDto
        {
            Id = Guid.NewGuid(),
            Items = [new OrderItemDto { Id = itemId, Quantity = int.MaxValue }]
        }]);

        result.Should().ContainSingle().Which.UnitCount.Should().Be(int.MaxValue);
        result.Single().OrderItemId.Should().Be(itemId);
    }

    [Fact]
    public async Task Round_advances_account_revision_once_and_replay_preserves_account_units()
    {
        AuthenticateAsRole(UserRole.Server);
        var tableId = await SeedTableAsync("A-REVISION");
        var openedResponse = await PostAsJsonAsync(
            "/api/table-service-sessions", new { tableId });
        var opened = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(openedResponse))!.Data!;
        opened.AccountRevision.Should().Be(1);
        opened.Bill.AccountRevision.Should().Be(1);

        await using var context = DatabaseFixture.CreateContext();
        var productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
        var operationId = Guid.NewGuid();
        var request = new
        {
            clientOperationId = operationId,
            releaseToKitchen = false,
            type = nameof(OrderType.DineIn),
            tableId,
            serviceSessionId = opened.ServiceSessionId,
            paymentState = "Unpaid",
            items = new[] { new { productId, quantity = 2 } }
        };

        var first = await PostAsJsonAsync("/api/staff/orders/round", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!.Data!;

        var afterCreate = await ReadSessionAsync(opened.ServiceSessionId);
        afterCreate.Version.Should().Be(opened.Version + 1);
        afterCreate.AccountRevision.Should().Be(opened.AccountRevision + 1);
        afterCreate.Bill.AccountRevision.Should().Be(afterCreate.AccountRevision);
        var accountLine = afterCreate.Bill.AccountItems.Should().ContainSingle().Subject;
        accountLine.OrderId.Should().Be(created.Id);
        accountLine.OrderItemId.Should().Be(created.Items.Single().Id);
        accountLine.ItemSnapshot.Quantity.Should().Be(2);
        accountLine.UnitCount.Should().Be(2);

        var replay = await PostAsJsonAsync("/api/staff/orders/round", request);
        var replayedOrder = (await ReadResponseAsync<ApiResponse<OrderDto>>(replay))!.Data!;
        replayedOrder.Id.Should().Be(created.Id);

        var afterReplay = await ReadSessionAsync(opened.ServiceSessionId);
        afterReplay.Version.Should().Be(afterCreate.Version);
        afterReplay.AccountRevision.Should().Be(afterCreate.AccountRevision);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync(order => order.ServiceSessionId == opened.ServiceSessionId))
            .Should().Be(1);
        (await verify.StaffOrderOperations.CountAsync(operation => operation.OperationId == operationId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Explicit_account_snapshot_preserves_recursive_lines_and_frozen_money_without_unit_allocation()
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var sideId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableNumber = 71,
            Status = TableServiceSessionStatus.Open,
            Version = 4,
            AccountRevision = 17,
            OpenedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableAccountRevisionTests)
        });
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"AC-{orderId:N}"[..16],
            Type = OrderType.DineIn,
            TableNumber = 71,
            ServiceSessionId = sessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 58m,
            Discount = 3m,
            Total = 55m,
            RemainingAmount = 55m,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableAccountRevisionTests)
        };
        order.Items.Add(new OrderItem
        {
            Id = rootId,
            OrderId = orderId,
            ProductName = "Frozen main",
            Quantity = 2,
            UnitPrice = 25m,
            ItemTotal = 50m,
            CreatedBy = nameof(TableAccountRevisionTests),
            IngredientSnapshots =
            {
                new OrderItemIngredient
                {
                    Id = Guid.NewGuid(),
                    OrderItemId = rootId,
                    IngredientId = Guid.NewGuid(),
                    IngredientName = "Historic sauce",
                    Quantity = 1,
                    SortOrder = 0,
                    IsAddOn = true,
                    CreatedBy = nameof(TableAccountRevisionTests)
                }
            }
        });
        order.Items.Add(new OrderItem
        {
            Id = sideId,
            OrderId = orderId,
            ParentOrderItemId = rootId,
            Kind = OrderItemKind.SideItem,
            ProductName = "Frozen side",
            Quantity = 1,
            UnitPrice = 4m,
            ItemTotal = 8m,
            CreatedBy = nameof(TableAccountRevisionTests)
        });
        order.Items.Add(new OrderItem
        {
            Id = nestedId,
            OrderId = orderId,
            ParentOrderItemId = sideId,
            Kind = OrderItemKind.BundleChild,
            ProductName = "Nested component",
            Quantity = 1,
            UnitPrice = 0m,
            ItemTotal = 0m,
            CreatedBy = nameof(TableAccountRevisionTests)
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var bill = await CreateAssembler(context).AssembleAsync(sessionId, CancellationToken.None);

        bill.Should().NotBeNull();
        bill!.AccountRevision.Should().Be(17);
        bill.Discount.Should().Be(3m);
        var accountLine = bill.AccountItems.Should().ContainSingle().Subject;
        accountLine.OrderId.Should().Be(orderId);
        accountLine.OrderItemId.Should().Be(rootId);
        accountLine.ItemSnapshot.ItemTotal.Should().Be(50m);
        accountLine.ItemSnapshot.IngredientCustomizations!.Single().IngredientName
            .Should().Be("Historic sauce");
        accountLine.UnitCount.Should().Be(2);
        var side = accountLine.ItemSnapshot.SideItems.Should().ContainSingle().Subject;
        side.Id.Should().Be(sideId);
        side.Kind.Should().Be(OrderItemKind.SideItem);
        side.SideItems.Should().ContainSingle().Which.Id.Should().Be(nestedId);
        typeof(TableBillAccountItemDto).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("OrderId", "OrderNumber", "OrderItemId", "ItemSnapshot", "UnitCount");
    }

    [Fact]
    public async Task Legacy_table_bill_does_not_invent_session_revision_or_account_units()
    {
        var orderId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"AC-{orderId:N}"[..16],
            Type = OrderType.DineIn,
            TableNumber = 72,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableAccountRevisionTests)
        });
        await context.SaveChangesAsync();

        var bill = await CreateAssembler(context).AssembleAsync(72, CancellationToken.None);

        bill.Should().NotBeNull();
        bill!.ServiceSessionId.Should().BeNull();
        bill.AccountRevision.Should().BeNull();
        bill.AccountItems.Should().BeEmpty();
    }

    private async Task<Guid> SeedTableAsync(string label)
    {
        var id = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = id,
            TableNumber = label,
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(TableAccountRevisionTests)
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<TableServiceSessionDto> ReadSessionAsync(Guid sessionId)
    {
        var response = await Client.GetAsync($"/api/table-service-sessions/{sessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(response))!.Data!;
    }

    private static TableBillAssembler CreateAssembler(ApplicationDbContext context) => new(
        context,
        new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance),
        NullLogger<TableBillAssembler>.Instance);
}
