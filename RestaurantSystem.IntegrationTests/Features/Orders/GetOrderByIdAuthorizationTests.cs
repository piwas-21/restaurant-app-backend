using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the ownership rule on GET /api/orders/{id}.
///
/// The endpoint was [Authorize]-only and its handler filtered on id alone, so any
/// authenticated customer could read any order — customer name, email, phone,
/// delivery address and payment rows included. The list endpoint (GetOrdersQuery)
/// had always scoped to the caller; only the single-order route was missing it.
///
/// The rule: staff read any order, a customer reads only their own, and a denied
/// read is answered with the ordinary not-found response so the endpoint cannot be
/// used to confirm which order ids exist.
/// </summary>
[Collection("Database Lane 1")]
public class GetOrderByIdAuthorizationTests : IntegrationTestBase
{
    /// <summary>
    /// Its OWN host per test: it posts to send-confirmation-email, whose "confirmation-email" per-IP window is host state Respawn cannot reset.
    /// </summary>
    protected override bool RequiresIsolatedHost => true;

    public GetOrderByIdAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>A second customer, so "not mine" is a real other account rather than the admin.</summary>
    private static readonly Guid OtherCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid OwnOrderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCustomerOrderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid GuestOrderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Order.UserId is a restricted FK to AspNetUsers, so the second customer must exist.
        context.Users.Add(new ApplicationUser
        {
            Id = OtherCustomerId,
            UserName = "other@example.com",
            NormalizedUserName = "OTHER@EXAMPLE.COM",
            Email = "other@example.com",
            NormalizedEmail = "OTHER@EXAMPLE.COM",
            EmailConfirmed = true,
            FirstName = "Other",
            LastName = "Customer",
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString()
        });

        context.Orders.AddRange(
            CreateOrder(OwnOrderId, "ORD-OWN", Guid.Parse(TestAuthHandler.UserId)),
            CreateOrder(OtherCustomerOrderId, "ORD-OTHER", OtherCustomerId),
            // Guest checkout: no account, so no owner. Reachable only through the
            // confirmation-email path, never through this read endpoint.
            CreateOrder(GuestOrderId, "ORD-GUEST", userId: null));

        await context.SaveChangesAsync();
    }

    private static Order CreateOrder(Guid id, string orderNumber, Guid? userId) => new()
    {
        Id = id,
        OrderNumber = orderNumber,
        UserId = userId,
        CustomerName = "Jane Doe",
        CustomerEmail = "jane@example.com",
        CustomerPhone = "+41 79 000 00 00",
        OrderDate = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Completed,
        SubTotal = 10m,
        Total = 10m,
        IsDeleted = false,
        CreatedAt = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        CreatedBy = "test",
    };

    [Fact]
    public async Task Owner_ReadingTheirOwnOrder_GetsTheOrder()
    {
        AuthenticateAsUser();

        var result = await GetOrder(OwnOrderId);

        result!.Success.Should().BeTrue();
        result.Data!.OrderNumber.Should().Be("ORD-OWN");
    }

    [Fact]
    public async Task Staff_ReadingAnotherCustomersOrder_GetsTheOrder()
    {
        AuthenticateAsAdmin();

        var result = await GetOrder(OtherCustomerOrderId);

        result!.Success.Should().BeTrue("staff run the till, kitchen and floor views over every order");
        result.Data!.OrderNumber.Should().Be("ORD-OTHER");
    }

    /// <summary>
    /// The non-admin staff roles, which Admin-only coverage would miss entirely: narrowing
    /// ICurrentUserService.IsStaff back to IsAdmin would break the cashier till, the kitchen
    /// display and the floor view while every other test in the suite still passed.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task EveryStaffRole_ReadingAnotherCustomersOrder_GetsTheOrder(UserRole role)
    {
        AuthenticateAsRole(role);

        var result = await GetOrder(OtherCustomerOrderId);

        result!.Success.Should().BeTrue($"{role} is back-of-house and reads any customer's order");
        result.Data!.OrderNumber.Should().Be("ORD-OTHER");
    }

    /// <summary>
    /// Customer is the one role that is NOT staff. Pinned explicitly because IsStaff is now
    /// shared with GetOrdersQuery, where widening it would silently unscope the list too.
    /// </summary>
    [Fact]
    public async Task CustomerRole_IsNotTreatedAsStaff()
    {
        AuthenticateAsRole(UserRole.Customer);

        var result = await GetOrder(OtherCustomerOrderId);

        result!.Success.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    /// <summary>
    /// The ownership flag is a server-side concern; it must not be settable from the wire.
    /// Both callers construct the query by hand today, so this pins the property against a
    /// future refactor to [FromQuery] binding (the prevailing convention for sibling queries).
    /// </summary>
    [Fact]
    public async Task EnforceOwnership_CannotBeTurnedOffFromTheQueryString()
    {
        AuthenticateAsUser();

        var response = await Client.GetAsync(
            $"/api/orders/{OtherCustomerOrderId}?enforceOwnership=false");
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<OrderDto>>(json, JsonOptions);

        result!.Success.Should().BeFalse("the caller must not be able to disable the ownership check");
        result.Data.Should().BeNull();
    }

    /// <summary>An unauthenticated caller has no route to order data at all.</summary>
    [Fact]
    public async Task Anonymous_ReadingAnyOrder_IsChallenged()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{GuestOrderId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the read route is [Authorize]; only the confirmation-email route is anonymous");
    }

    [Fact]
    public async Task Customer_ReadingAnotherCustomersOrder_GetsNotFound()
    {
        AuthenticateAsUser();

        var result = await GetOrder(OtherCustomerOrderId);

        result!.Success.Should().BeFalse();
        result.Data.Should().BeNull("no field of another customer's order may reach the caller");
    }

    /// <summary>
    /// The anti-enumeration property: a real-but-unauthorised id and an id that does not
    /// exist must be indistinguishable to the caller. If these ever diverge (a 403 here,
    /// a different message, a different status) the endpoint becomes an oracle for
    /// discovering valid order ids.
    /// </summary>
    [Fact]
    public async Task Customer_DeniedRead_IsIndistinguishableFromAMissingOrder()
    {
        AuthenticateAsUser();

        var deniedResponse = await Client.GetAsync($"/api/orders/{OtherCustomerOrderId}");
        var missingResponse = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        deniedResponse.StatusCode.Should().Be(missingResponse.StatusCode);
        deniedResponse.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a 403 would confirm the order id exists");

        var deniedBody = await deniedResponse.Content.ReadAsStringAsync();
        var missingBody = await missingResponse.Content.ReadAsStringAsync();
        deniedBody.Should().Be(missingBody);
    }

    /// <summary>
    /// Guest orders have no owner, so no logged-in customer may claim them by id.
    /// The guest's own confirmation page does not depend on this route — it falls back
    /// to the order number it already holds (see the frontend confirmation page tests).
    /// </summary>
    [Fact]
    public async Task Customer_ReadingAGuestOrder_GetsNotFound()
    {
        AuthenticateAsUser();

        var result = await GetOrder(GuestOrderId);

        result!.Success.Should().BeFalse(
            "a null owner must not match a null-or-any caller, or every guest order leaks");
        result.Data.Should().BeNull();
    }

    /// <summary>
    /// The one legitimate non-owner reader: the [AllowAnonymous] confirmation-email
    /// endpoint (ADR-004) resolves the order server-side to address the email. It passes
    /// EnforceOwnership: false, so tightening the read path must not break guest checkout.
    /// The order never reaches the caller — only the mail already addressed to it.
    /// </summary>
    [Fact]
    public async Task GuestConfirmationEmail_StillResolvesTheGuestOrder()
    {
        // A real tokenless request: TestAuthHandler authenticates everything by default, so
        // clearing the Authorization header alone would still arrive as the Customer identity
        // and would not exercise the guest path this test exists to protect.
        AuthenticateAsAnonymous();

        var response = await Client.PostAsync(
            $"/api/orders/{GuestOrderId}/send-confirmation-email", content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "a throttled request would not exercise the lookup this test is about");

        // Sending may still fail on SMTP in the test environment; that is a different
        // failure from "Order not found", which is what a broken lookup would report.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Order not found",
            "the guest order must remain resolvable for the confirmation email");
    }

    private async Task<ApiResponse<OrderDto>?> GetOrder(Guid orderId)
    {
        var response = await Client.GetAsync($"/api/orders/{orderId}");
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<OrderDto>>(json, JsonOptions);
    }
}
