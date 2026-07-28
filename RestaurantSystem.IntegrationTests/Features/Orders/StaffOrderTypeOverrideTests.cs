using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// §9.6 — a staff member may accept an order containing items the channel does not allow
/// (warn-and-allow: a waiter genuinely does need to plate a takeaway-only item for a guest at a
/// table). Until now the only trace was an application-log line, which no owner reads and which
/// rotates; the override is now stamped on the order itself.
/// <para>
/// The customer-refusal case is asserted alongside on purpose. The guard's two branches are one
/// decision, and the failure mode that matters is the persistence change quietly widening the staff
/// branch to everyone — a test suite that only proves "staff can" would not notice.
/// </para>
/// </summary>
public class StaffOrderTypeOverrideTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);
    private const string BlockedProductName = "§9.6 Takeaway-Only Wrap";

    private Guid _blockedProductId;
    private Guid _unrestrictedProductId;

    public StaffOrderTypeOverrideTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Staff_accepting_a_blocked_item_has_the_override_persisted_on_the_order()
    {
        AuthenticateAsAdmin();

        var order = await CreateOrderAsync(_blockedProductId, OrderType.DineIn);

        var stored = await StoredOrderAsync(order.Id);
        // The exact id, not merely "not empty": `GetAuditIdentifier()` falls back to "System" for an
        // unauthenticated caller, so a NotBeNullOrEmpty assertion is equally happy with an audit
        // trail that has lost the actor — the one failure this record exists to prevent.
        stored.OrderTypeOverrideBy.Should().Be(TestAuthHandler.AdminUserId,
            "the accepting staff member is the point of the record");
        stored.OrderTypeOverrideItems.Should().Be(BlockedProductName,
            "the owner needs to know WHAT was accepted, not merely that something was");
    }

    [Fact]
    public async Task The_override_is_readable_back_on_the_order_dto()
    {
        // A column no surface can read is a column nobody trusts. Additive and read-only — no client
        // is required to render it, but the data must be reachable without database access.
        AuthenticateAsAdmin();
        var order = await CreateOrderAsync(_blockedProductId, OrderType.DineIn);

        var fetched = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{order.Id}");

        fetched!.Data!.OrderTypeOverrideBy.Should().Be(TestAuthHandler.AdminUserId);
        fetched.Data.OrderTypeOverrideItems.Should().Be(BlockedProductName);
    }

    /// <summary>
    /// A blocked BUNDLE COMPONENT must be named too. The guard flattens <c>ChildItems</c> to decide,
    /// so the persisted string follows the same walk — and every other test here uses a single root
    /// product, which is exactly how the child half of §9.3 and §9.15 came to be missed twice.
    /// </summary>
    [Fact]
    public async Task A_blocked_CHILD_item_is_named_in_the_record()
    {
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync("/api/Orders", new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Walk-in",
            Items =
            [
                new CreateOrderItemDto
                {
                    ProductId = _unrestrictedProductId,
                    Quantity = 1,
                    UnitPrice = 10m,
                    ChildItems = [new CreateOrderItemDto { ProductId = _blockedProductId, Quantity = 1, UnitPrice = 0m }]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;

        (await StoredOrderAsync(order.Id)).OrderTypeOverrideItems.Should().Be(BlockedProductName,
            "the root was orderable — only the component was not");
    }

    [Fact]
    public async Task An_ordinary_staff_order_records_no_override()
    {
        // The field must mean something. If every staff order carried a value it would be noise, and
        // an owner filtering on it would learn nothing.
        AuthenticateAsAdmin();

        var order = await CreateOrderAsync(_unrestrictedProductId, OrderType.DineIn);

        var stored = await StoredOrderAsync(order.Id);
        stored.OrderTypeOverrideBy.Should().BeNull();
        stored.OrderTypeOverrideItems.Should().BeNull();
    }

    [Fact]
    public async Task A_staff_order_on_an_ALLOWED_channel_records_no_override()
    {
        // Same product, permitted channel: nothing was overridden, so nothing is recorded.
        AuthenticateAsAdmin();

        var order = await CreateOrderAsync(_blockedProductId, OrderType.Takeaway);

        (await StoredOrderAsync(order.Id)).OrderTypeOverrideBy.Should().BeNull();
    }

    [Fact]
    public async Task A_customer_is_still_REFUSED_rather_than_recorded()
    {
        AuthenticateAsUser();

        var response = await SendCreateOrderAsync(_blockedProductId, OrderType.DineIn);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await StoredOrderCountAsync()).Should().Be(0, "a refused order must not exist at all");
    }

    private async Task<OrderDto> CreateOrderAsync(Guid productId, OrderType orderType)
    {
        var response = await SendCreateOrderAsync(productId, orderType);
        response.EnsureSuccessStatusCode();
        var body = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    /// <summary>
    /// Over HTTP, not through the mediator: "is the caller staff" is answered from the HTTP context,
    /// so a scope resolved straight from <c>Factory.Services</c> has no principal at all and every
    /// caller looks like an anonymous guest. A mediator-level test of this guard silently asserts the
    /// customer branch twice.
    /// </summary>
    private Task<HttpResponseMessage> SendCreateOrderAsync(Guid productId, OrderType orderType) =>
        PostAsJsonAsync("/api/Orders", new CreateOrderCommand
        {
            Type = orderType,
            TableNumber = orderType == OrderType.DineIn ? 7 : null,
            CustomerName = "Walk-in",
            Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1, UnitPrice = 10m }]
        });

    private async Task<int> StoredOrderCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.CountAsync();
    }

    private async Task<Order> StoredOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var blocked = new Product
        {
            Name = BlockedProductName,
            BasePrice = 10.00m,
            IsActive = true,
            IsAvailable = true,
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test"
        };
        var unrestricted = new Product
        {
            Name = "§9.6 Anything Goes",
            BasePrice = 10.00m,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test"
        };

        context.AddRange(blocked, unrestricted);
        await context.SaveChangesAsync();

        _blockedProductId = blocked.Id;
        _unrestrictedProductId = unrestricted.Id;
    }
}
