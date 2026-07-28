using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// §9.19 — <c>GET /api/Orders/{id}</c> was <c>[Authorize]</c> and nothing else, while
/// <c>GetOrdersQuery</c> (same feature, one file over) deliberately scopes the LIST to the caller.
/// Any signed-in customer who guessed or enumerated an order id therefore read the whole order:
/// name, email, phone, delivery address, payment rows. Authentication was checked; authorization
/// was not.
/// <para>
/// The negative cases are the point, and both halves have to be asserted together: a check that
/// refuses everyone would pass a "stranger is refused" test on its own, which is why the owner and
/// staff cases sit beside it.
/// </para>
/// <para>
/// <b>NOT asserted here, deliberately:</b> that an anonymous caller gets 401. `TestAuthHandler`
/// synthesizes a Customer principal on EVERY request, so this harness has no anonymous state — a
/// test for it would assert 200 and prove the opposite of its name. The 401 comes from
/// <c>[Authorize]</c> on the action, which is a controller-attribute fact, not a handler one.
/// </para>
/// </summary>
public class OrderDetailOwnershipTests : IntegrationTestBase
{
    private Guid _ownedByTestUser;
    private Guid _ownedBySomeoneElse;
    private Guid _guestOrder;

    public OrderDetailOwnershipTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_customer_reads_their_OWN_order()
    {
        AuthenticateAsUser();

        var response = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_ownedByTestUser}");

        response!.Success.Should().BeTrue();
        response.Data!.Id.Should().Be(_ownedByTestUser);
    }

    [Fact]
    public async Task A_customer_is_refused_someone_ELSES_order()
    {
        AuthenticateAsUser();

        var response = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_ownedBySomeoneElse}");

        response!.Success.Should().BeFalse();
        response.Data.Should().BeNull("a refused order must not leak so much as a field");
    }

    /// <summary>
    /// The wording matters as much as the refusal: a 403, or any message distinguishable from the
    /// missing-id one, confirms the id EXISTS — which is half of what an enumerator is after.
    /// </summary>
    [Fact]
    public async Task A_refused_order_is_indistinguishable_from_a_missing_one()
    {
        AuthenticateAsUser();

        var refused = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_ownedBySomeoneElse}");
        var missing = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{Guid.NewGuid()}");

        refused!.Errors.Should().BeEquivalentTo(missing!.Errors);
        refused.Message.Should().Be(missing.Message);
    }

    /// <summary>
    /// A guest order carries <c>UserId == null</c>. The check is stated positively — the caller must
    /// have an id AND it must match — precisely so this cannot resolve as "null equals null, so they
    /// own it" for a caller whose id claim is missing or unparseable.
    /// </summary>
    [Fact]
    public async Task A_customer_is_refused_a_GUEST_order()
    {
        AuthenticateAsUser();

        var response = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_guestOrder}");

        response!.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(UserRole.Admin))]
    public async Task Staff_read_any_order(string role)
    {
        role.Should().Be(nameof(UserRole.Admin), "the test auth handler issues Admin for the staff header");
        AuthenticateAsAdmin();

        var stranger = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_ownedBySomeoneElse}");
        var guest = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{_guestOrder}");

        stranger!.Success.Should().BeTrue("the cashier and kitchen surfaces read orders they do not own");
        guest!.Success.Should().BeTrue();
    }

    /// <summary>
    /// The gate must NOT reach the notification path. <c>OrderEmailController</c> is deliberately
    /// <c>[AllowAnonymous]</c> (ADR-004: guest checkout has no bearer token yet) and dispatches this
    /// same query, so a scope-blind check would refuse every request and guest confirmation emails
    /// would silently stop being sent.
    /// <para>
    /// The existing <c>SendConfirmationEmailAuthTests</c> could not have caught that: it posts an
    /// UNKNOWN order id, and "refused" and "missing" are the same response by design. This one uses a
    /// REAL guest order and asserts on the distinction — the order must resolve, whatever SMTP then
    /// does with it (there is no mail server in the harness, so the send itself fails, and that
    /// failure has its own distinct message).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_notification_path_still_resolves_an_order_it_does_not_own()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.PostAsync($"/api/orders/{_guestOrder}/send-confirmation-email", content: null);
        var body = await ReadResponseAsync<ApiResponse<string>>(response);

        // Null-safe: a successful response carries no Errors at all, and "no errors" is a pass here.
        // The assertion is about ONE error in particular — the refusal — not about the send outcome,
        // which depends on whether the harness has a mail server.
        (body!.Errors ?? []).Should().NotContain("Order not found",
            "the anonymous notification path must reach the order, or guest confirmation emails stop");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var mine = NewOrder("§9.19-MINE", Guid.Parse(TestAuthHandler.UserId));
        var theirs = NewOrder("§9.19-THEIRS", Guid.Parse(TestAuthHandler.AdminUserId));
        var guest = NewOrder("§9.19-GUEST", userId: null);

        context.AddRange(mine, theirs, guest);
        await context.SaveChangesAsync();

        _ownedByTestUser = mine.Id;
        _ownedBySomeoneElse = theirs.Id;
        _guestOrder = guest.Id;
    }

    private static Order NewOrder(string number, Guid? userId) => new()
    {
        OrderNumber = number,
        UserId = userId,
        CustomerName = "Someone",
        CustomerEmail = "someone@example.com",
        CustomerPhone = "+41000000000",
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        Total = 10m,
        CreatedBy = "test"
    };
}
