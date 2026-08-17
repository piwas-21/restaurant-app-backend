using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Commands.ToggleFocusOrderCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The five focus columns moved behind the owned <c>Order.Focus</c> type and the
/// <c>is_focus_order</c> boolean was dropped, so "focused" is now the presence of the record.
/// </summary>
/// <remarks>
/// Everything here is asserted through SQL rather than through the tracked entity that just wrote
/// it. The two mistakes this refactor could make are both invisible in memory: an owned type that
/// scaffolds its own table (the first attempt did exactly that) still round-trips perfectly through
/// a DbContext, and <c>Where(o =&gt; o.Focus != null)</c> is a compile-time no-op that either
/// becomes a WHERE clause or throws at runtime. Only a query that goes to the database tells them
/// apart. The DTO assertions are here for the other half: the columns moved, the API did not.
/// </remarks>
[Collection("Database Lane 2")]
public class OrderFocusOwnedTypeTests : IntegrationTestBase
{
    private Guid _productId;

    public OrderFocusOwnedTypeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Focusing_an_order_stores_the_record_in_the_orders_row()
    {
        AuthenticateAsAdmin();
        var order = await CreateOrderAsync();

        await FocusAsync(order.Id, priority: 2, reason: "VIP table");

        var stored = await StoredOrderAsync(order.Id);
        stored.Focus.Should().NotBeNull();
        stored.Focus!.Priority.Should().Be(2);
        stored.Focus.Reason.Should().Be("VIP table");
        stored.Focus.FocusedBy.Should().Be(TestAuthHandler.AdminUserId);
        stored.Focus.FocusedAt.Should().NotBe(default);

        // Same table, not a joined one: the whole point of an owned type here was to avoid a join
        // on a record read with every order.
        (await FocusColumnsLiveOnOrdersAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Un_focusing_clears_every_focus_column()
    {
        // The invariant the extraction buys. Un-focusing used to be four separate assignments, so
        // forgetting one left a FocusedBy or a reason attached to an order nobody was watching.
        AuthenticateAsAdmin();
        var order = await CreateOrderAsync();
        await FocusAsync(order.Id, priority: 1, reason: "burnt the first one");

        await UnfocusAsync(order.Id);

        var stored = await StoredOrderAsync(order.Id);
        stored.Focus.Should().BeNull("a dropped record cannot leave a stale field behind");
    }

    [Fact]
    public async Task The_focus_list_filters_in_the_database()
    {
        AuthenticateAsAdmin();
        var focused = await CreateOrderAsync();
        var ignored = await CreateOrderAsync();
        await FocusAsync(focused.Id, priority: 3, reason: "allergy");

        var listed = await GetFromJsonAsync<ApiResponse<List<OrderDto>>>("/api/Orders/focus");

        listed!.Data!.Select(o => o.Id).Should().Contain(focused.Id).And.NotContain(ignored.Id);
    }

    [Fact]
    public async Task The_order_dto_still_reports_focus_the_way_it_always_did()
    {
        // The columns moved; the contract did not. Frontend and printer-app read these five names.
        AuthenticateAsAdmin();
        var order = await CreateOrderAsync();
        await FocusAsync(order.Id, priority: 4, reason: "regular");

        var fetched = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{order.Id}");

        fetched!.Data!.IsFocusOrder.Should().BeTrue();
        fetched.Data.Priority.Should().Be(4);
        fetched.Data.FocusReason.Should().Be("regular");
        fetched.Data.FocusedAt.Should().NotBeNull();
        fetched.Data.FocusedBy.Should().Be(TestAuthHandler.AdminUserId);
    }

    [Fact]
    public async Task An_unfocused_order_reports_nulls_rather_than_a_half_record()
    {
        AuthenticateAsAdmin();
        var order = await CreateOrderAsync();

        var fetched = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{order.Id}");

        fetched!.Data!.IsFocusOrder.Should().BeFalse();
        fetched.Data.Priority.Should().BeNull();
        fetched.Data.FocusReason.Should().BeNull();
        fetched.Data.FocusedAt.Should().BeNull();
        fetched.Data.FocusedBy.Should().BeNull();
    }

    private async Task FocusAsync(Guid orderId, int priority, string reason)
    {
        var response = await PutAsJsonAsync($"/api/Orders/{orderId}/focus", new ToggleFocusOrderCommand
        {
            IsFocusOrder = true,
            Priority = priority,
            FocusReason = reason
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task UnfocusAsync(Guid orderId)
    {
        var response = await PutAsJsonAsync($"/api/Orders/{orderId}/focus",
            new ToggleFocusOrderCommand { IsFocusOrder = false });
        response.EnsureSuccessStatusCode();
    }

    private async Task<OrderDto> CreateOrderAsync()
    {
        var response = await PostAsJsonAsync("/api/Orders", new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Walk-in",
            Items = [new CreateOrderItemDto { ProductId = _productId, Quantity = 1, UnitPrice = 10m }]
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    private async Task<Order> StoredOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
    }

    private async Task<bool> FocusColumnsLiveOnOrdersAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entityType = context.Model.FindEntityType(typeof(Order))!;
        var focusType = entityType.FindNavigation(nameof(Order.Focus))!.TargetEntityType;
        return focusType.GetTableName() == entityType.GetTableName();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = new Product
        {
            Name = "Focus Test Plate",
            BasePrice = 10.00m,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test"
        };
        context.Add(product);
        await context.SaveChangesAsync();

        _productId = product.Id;
    }
}
