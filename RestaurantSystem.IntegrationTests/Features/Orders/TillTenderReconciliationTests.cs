using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The read side of till operation idempotency. A client that loses the POST response can ask
/// whether the exact operation committed without replaying a guessed tender payload. The answer
/// is a structured Committed/Unknown result and carries the server's current order projection.
/// </summary>
[Collection("Database Lane 3")]
public class TillTenderReconciliationTests : IntegrationTestBase
{
    private Guid _orderId;
    private Guid _otherOrderId;

    public TillTenderReconciliationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Anonymous_lookup_is_challenged()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync(OperationUri(_orderId, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_cannot_reconcile_a_staff_payment_operation()
    {
        AuthenticateAsUser();

        var response = await Client.GetAsync(OperationUri(_orderId, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task Every_staff_role_can_reconcile_an_unknown_operation(UserRole role)
    {
        AuthenticateAsRole(role);

        var response = await Client.GetAsync(OperationUri(_orderId, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} is an authenticated staff caller");
        var body = await ReadResponseAsync<ApiResponse<PaymentOperationLookupDto>>(response);
        body!.Success.Should().BeTrue();
        body.Data!.Status.Should().Be(PaymentOperationLookupStatus.Unknown);
        body.Data.Payment.Should().BeNull();
        body.Data.Order!.OrderNumber.Should().Be("RECONCILE-1");
    }

    [Fact]
    public async Task Unknown_operation_returns_current_order_without_a_payment()
    {
        AuthenticateAsRole(UserRole.Cashier);
        var operationId = Guid.NewGuid();

        var response = await Client.GetAsync(OperationUri(_orderId, operationId));
        var body = await ReadResponseAsync<ApiResponse<PaymentOperationLookupDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue("unknown is a valid reconciliation answer, not an HTTP failure");
        body.Data!.OperationId.Should().Be(operationId);
        body.Data.Status.Should().Be(PaymentOperationLookupStatus.Unknown);
        body.Data.Payment.Should().BeNull();
        body.Data.Order!.OrderNumber.Should().Be("RECONCILE-1");
        body.Data.Order.TotalPaid.Should().Be(0m, "the authoritative order must not invent a tender");
    }

    [Fact]
    public async Task Committed_operation_returns_original_payment_and_authoritative_order()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var post = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m });
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.GetAsync(OperationUri(_orderId, operationId));
        var body = await ReadResponseAsync<ApiResponse<PaymentOperationLookupDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data!.OperationId.Should().Be(operationId);
        body.Data.Status.Should().Be(PaymentOperationLookupStatus.Committed);
        body.Data.Payment.Should().NotBeNull();
        body.Data.Payment!.OperationId.Should().Be(operationId);
        body.Data.Payment.PaymentMethod.Should().Be("Cash");
        body.Data.Payment.Amount.Should().Be(10m);
        body.Data.Order.Should().NotBeNull();
        body.Data.Order!.OrderNumber.Should().Be("RECONCILE-1");
        body.Data.Order.TotalPaid.Should().Be(10m, "the lookup must return the committed order summary");
        body.Data.Order.Payments.Should().ContainSingle(p => p.OperationId == operationId);
    }

    [Fact]
    public async Task An_operation_committed_for_another_order_is_unknown_on_this_order()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var post = await Client.PostAsJsonAsync(
            $"/api/Orders/{_otherOrderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 4m });
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        // Operation ids are tenant-local, and globally unique inside this tenant database. The
        // route order is still part of the lookup scope: do not disclose another order's tender.
        var response = await Client.GetAsync(OperationUri(_orderId, operationId));
        var body = await ReadResponseAsync<ApiResponse<PaymentOperationLookupDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data!.Status.Should().Be(PaymentOperationLookupStatus.Unknown);
        body.Data.Payment.Should().BeNull();
        body.Data.Order!.OrderNumber.Should().Be("RECONCILE-1");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = CreateOrder("RECONCILE-1");
        var other = CreateOrder("RECONCILE-2");
        context.Orders.AddRange(order, other);
        await context.SaveChangesAsync();
        _orderId = order.Id;
        _otherOrderId = other.Id;
    }

    private static Order CreateOrder(string orderNumber) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Walk-in",
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        Total = 10m,
        CreatedBy = "test"
    };

    private static string OperationUri(Guid orderId, Guid operationId) =>
        $"/api/Orders/{orderId}/payments/operations/{operationId}";
}
