using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the one path that must stay reachable with NO credentials at all.
///
/// #256 made the order mutation routes staff-only at the controller attribute. That placement is
/// load-bearing rather than stylistic, and nothing recorded why: OrderQuickActionsController
/// dispatches <c>UpdateOrderStatusCommand</c> and <c>CancelOrderCommand</c> from its
/// [AllowAnonymous] email-link actions, so the same rule expressed as an <c>IsStaff</c> check
/// INSIDE either handler — the obvious place, and where #258's ownership fix for the read route
/// lives — sees no authenticated user here and breaks every quick-confirm / quick-cancel link
/// already sitting in an inbox.
///
/// Before this file, quick-confirm and quick-cancel appeared in no test anywhere in the suite, so
/// that refactor would have gone green. These are the tests that fail instead.
/// </summary>
public class OrderEmailLinkAuthorizationTests : IntegrationTestBase
{
    public OrderEmailLinkAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid QuickCancelOrderId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid QuickConfirmOrderId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    // Distinct suffixes: the quick-action lookup resolves an order by SUBSTRING match and takes the
    // first row, so two numbers sharing a prefix would make these tests select each other's order.
    private const string QuickCancelOrderNumber = "ORDLINK-CANCEL";
    private const string QuickConfirmOrderNumber = "ORDLINK-CONFIRM";

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Both actions only act on a Pending order. Two rows so the tests cannot contend.
        // Guest orders (UserId null) — the quick-action links are emailed for orders placed
        // without an account too, and nothing on this path depends on an owner.
        context.Orders.AddRange(
            CreateOrder(QuickCancelOrderId, QuickCancelOrderNumber),
            CreateOrder(QuickConfirmOrderId, QuickConfirmOrderNumber));

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// CustomerEmail is deliberately null: both handlers fire a real SMTP send when it is set, and
    /// these tests are about reachability, not mail.
    /// </summary>
    private static Order CreateOrder(Guid id, string orderNumber) => new()
    {
        Id = id,
        OrderNumber = orderNumber,
        UserId = null,
        CustomerName = "Jane Doe",
        CustomerEmail = null,
        OrderDate = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 10m,
        Total = 10m,
        IsDeleted = false,
        CreatedAt = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        CreatedBy = "test",
    };

    [Fact]
    public async Task QuickCancelLink_StillCancels_WithNoCredentialsAtAll()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{QuickCancelOrderNumber}/quick-cancel");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Order Cancelled");
        (await LoadOrder(QuickCancelOrderId)).Status.Should().Be(OrderStatus.Cancelled,
            "the anonymous email-link path must keep reaching CancelOrderCommand");
    }

    /// <summary>
    /// The other half. Covered separately because quick-cancel alone leaves
    /// UpdateOrderStatusCommand unpinned, and that is the more-used of the two commands.
    /// minutes stays under the controller's 10-minute delay threshold so this lands on
    /// Confirmed rather than PendingApproval.
    /// </summary>
    [Fact]
    public async Task QuickConfirmLink_StillConfirms_WithNoCredentialsAtAll()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{QuickConfirmOrderNumber}/quick-confirm?minutes=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadOrder(QuickConfirmOrderId)).Status.Should().Be(OrderStatus.Confirmed,
            "the anonymous email-link path must keep reaching UpdateOrderStatusCommand");
    }

    private async Task<Order> LoadOrder(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
    }
}
