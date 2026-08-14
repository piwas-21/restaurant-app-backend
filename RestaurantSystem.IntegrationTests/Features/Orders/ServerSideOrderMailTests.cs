using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// GAP-11 (EMAIL-SPEC-TENANT-APP §4) — an order's mail is a consequence of the order existing.
///
/// <para>
/// Before this, both the guest's receipt (M7) and the restaurant's new-order alert (M14) were sent
/// only if the browser called <c>POST /api/orders/{id}/send-confirmation-email</c> after checkout
/// (<c>frontend/src/hooks/checkout/useCheckoutReview.ts:134</c>). A guest who closed the tab
/// silently cost the restaurant its only email notice of a real order, and nothing anywhere
/// recorded that a mail was owed.
/// </para>
///
/// <para>
/// Every test below posts an order and NEVER calls the confirmation endpoint — that omission is the
/// point. The assertions read the <c>outbound_emails</c> claim rows rather than a mock, because the
/// claim is also what makes GAP-12 idempotency true, and asserting on it pins both at once.
/// </para>
/// </summary>
public class ServerSideOrderMailTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;
    private const string GuestEmail = "guest@example.com";

    private Guid _pizzaId;

    public ServerSideOrderMailTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// The regression test for the gap itself: no client call, and both mails still go out.
    /// </summary>
    [Fact]
    public async Task Placing_an_order_sends_both_mails_with_no_client_call()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(GuestEmail);

        var receipt = await WaitForClaimAsync(OutboundEmailTypes.OrderReceived, orderId);
        receipt.Should().NotBeNull("the guest's receipt must not depend on their tab staying open");
        receipt!.SentAt.Should().NotBeNull();

        var adminAlert = await WaitForClaimAsync(OutboundEmailTypes.OrderAdminAlert, orderId);
        adminAlert.Should().NotBeNull(
            "the restaurant's only email notice of a new order must not depend on the guest's browser");
        adminAlert!.SentAt.Should().NotBeNull();
    }

    /// <summary>
    /// GAP-12. The endpoint the browser still calls is now a resend, and a resend of an already
    /// sent mail is a no-op — whether the client's call is a replay or merely late. Asserted on
    /// <c>SentAt</c> rather than on a row count: a duplicate send that reused the claim row would
    /// leave the count at 1 and move the timestamp.
    /// </summary>
    [Fact]
    public async Task The_legacy_confirmation_endpoint_does_not_send_a_second_time()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(GuestEmail);
        var firstSend = await WaitForClaimAsync(OutboundEmailTypes.OrderReceived, orderId);
        firstSend!.SentAt.Should().NotBeNull();

        var replay = await Client.PostAsync($"/api/orders/{orderId}/send-confirmation-email", content: null);

        replay.StatusCode.Should().Be(HttpStatusCode.OK, "an already-sent mail is a no-op, not an error");

        var claims = await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived);
        claims.Should().HaveCount(1);
        claims[0].SentAt.Should().Be(firstSend.SentAt, "the mail was not sent a second time");
    }

    /// <summary>
    /// GAP-13. An order with no customer email used to be mailed to the literal
    /// <c>noemail@example.com</c>. Harmless-ish while a browser had to ask for it; a guaranteed
    /// hard bounce on every emailless order once the server sends unprompted.
    /// </summary>
    [Fact]
    public async Task An_order_with_no_customer_email_sends_the_guest_nothing()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(customerEmail: null);

        // The admin alert is the control: it is queued for this same order, so its arrival proves
        // the mail path ran at all and the absent receipt is a decision rather than a dead path.
        (await WaitForClaimAsync(OutboundEmailTypes.OrderAdminAlert, orderId)).Should().NotBeNull();
        (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived)).Should().BeEmpty();
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private async Task<Guid> PlaceOrderAsync(string? customerEmail)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.Takeaway),
            customerName = "Guest",
            customerEmail,
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = nameof(PaymentMethod.Cash), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Orders.AsNoTracking().SingleAsync()).Id;
    }

    /// <summary>
    /// Polls, because the admin alert is dispatched on a detached task by design (it must never be
    /// able to delay or fail a guest's order). The guest receipt is awaited inside the request, so
    /// for that one the first pass already sees it.
    /// </summary>
    private async Task<OutboundEmail?> WaitForClaimAsync(string emailType, Guid orderId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var claims = await ClaimsForAsync(orderId, emailType);
            if (claims.Count > 0 && claims[0].SentAt != null)
            {
                return claims[0];
            }

            await Task.Delay(100);
        }

        return null;
    }

    private async Task<List<OutboundEmail>> ClaimsForAsync(Guid orderId, string emailType)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboundEmails.AsNoTracking()
            .Where(e => e.EntityId == orderId && e.EmailType == emailType)
            .ToListAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.AsNoTracking().FirstAsync(p => p.Name == "Test Pizza")).Id;
    }
}
