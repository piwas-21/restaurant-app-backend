using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class ServerTakeawayOrderTests : IntegrationTestBase
{
    private Guid _productId;

    public ServerTakeawayOrderTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new TenantModules(
            Options.Create(new ModuleSettings { Enabled = "core,server", Enforce = true }),
            NullLogger<TenantModules>.Instance));
    }

    [Fact]
    public async Task Server_can_quote_takeaway_without_table_session_or_address()
    {
        AuthenticateAsRole(UserRole.Server);

        var response = await PostAsJsonAsync("/api/staff/orders/quote", TakeawayBody());
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.Type.Should().Be(nameof(OrderType.Takeaway));
        body.Data.TableId.Should().BeNull();
        body.Data.TableNumber.Should().BeNull();
        body.Data.ServiceSessionId.Should().BeNull();
        body.Data.DeliveryAddress.Should().BeNull();
        body.Data.PaymentStatus.Should().Be(nameof(PaymentStatus.Pending));
        body.Data.TotalPaid.Should().Be(0m);
        body.Data.RemainingAmount.Should().Be(body.Data.Total);

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0, "quoting must not persist an order");
    }

    [Fact]
    public async Task Server_can_create_released_unpaid_takeaway_and_replay_the_same_operation()
    {
        AuthenticateAsRole(UserRole.Server);
        var operationId = Guid.NewGuid();
        var request = TakeawayBody(operationId, releaseToKitchen: true);

        var first = await PostAsJsonAsync("/api/staff/orders", request);
        var firstBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!;

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        firstBody.Success.Should().BeTrue();
        firstBody.Data.Should().NotBeNull();
        firstBody.Data!.Type.Should().Be(nameof(OrderType.Takeaway));
        firstBody.Data.TableId.Should().BeNull();
        firstBody.Data.TableNumber.Should().BeNull();
        firstBody.Data.ServiceSessionId.Should().BeNull();
        firstBody.Data.DeliveryAddress.Should().BeNull();
        firstBody.Data.PaymentStatus.Should().Be(nameof(PaymentStatus.Pending));
        firstBody.Data.TotalPaid.Should().Be(0m);
        firstBody.Data.RemainingAmount.Should().Be(firstBody.Data.Total);
        firstBody.Data.IsKitchenReleased.Should().BeTrue();
        firstBody.Data.Status.Should().Be(nameof(OrderStatus.Confirmed));

        var retry = await PostAsJsonAsync("/api/staff/orders", request);
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        retryBody.Success.Should().BeTrue();
        retryBody.Data!.Id.Should().Be(firstBody.Data.Id);
        retryBody.Data.IsKitchenReleased.Should().BeTrue();

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        var stored = await context.Orders.SingleAsync();
        stored.Type.Should().Be(OrderType.Takeaway);
        stored.ServiceSessionId.Should().BeNull();
        stored.DeliveryAddress.Should().BeNull();
        stored.IsKitchenReleased.Should().BeTrue();
        stored.TotalPaid.Should().Be(0m);
        stored.RemainingAmount.Should().Be(stored.Total);
    }

    private object TakeawayBody(Guid? operationId = null, bool? releaseToKitchen = null) => new
    {
        clientOperationId = operationId,
        releaseToKitchen,
        type = nameof(OrderType.Takeaway),
        paymentState = nameof(StaffOrderPaymentState.Unpaid),
        items = new[] { new { productId = _productId, quantity = 1 } }
    };

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
    }
}
