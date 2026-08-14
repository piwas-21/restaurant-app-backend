using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
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
/// Every test posts an order and NEVER calls the confirmation endpoint — that omission is the
/// point. Assertions are made against a <b>recording <c>IEmailService</c></b>, not against the
/// ledger rows: an implementation that mailed the guest without ever consulting the ledger would
/// satisfy the rows and fail the people. The ledger's own semantics are pinned separately in
/// <c>Common/OutboundEmailLedgerTests.cs</c>.
/// </para>
/// </summary>
public class ServerSideOrderMailTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;
    private const string GuestEmail = "guest@example.com";

    private readonly Mock<IEmailService> _email = new();
    private Guid _pizzaId;

    public ServerSideOrderMailTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Singleton, and deliberately not scoped: both mails are dispatched from detached tasks that
    /// resolve their own scope, so a scoped double would record one set of calls and the assertions
    /// would read another.
    /// </summary>
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);
    }

    /// <summary>The regression test for the gap: no client call, and both mails still go out.</summary>
    [Fact]
    public async Task Placing_an_order_sends_both_mails_with_no_client_call()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(OrderType.Takeaway, GuestEmail);

        await WaitForMailsAsync(orderId);

        VerifyGuestReceipts(orderId, Times.Once());
        VerifyAdminAlerts(orderId, Times.Once());
    }

    /// <summary>
    /// GAP-12 — the endpoint the browser still calls is a resend, and a resend of an already sent
    /// mail sends nothing. This is the case that made idempotency a requirement rather than a
    /// nicety: the old client call still exists in every shipped frontend.
    /// </summary>
    [Fact]
    public async Task The_legacy_confirmation_endpoint_does_not_send_a_second_time()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(OrderType.Takeaway, GuestEmail);
        await WaitForMailsAsync(orderId);

        var replay = await Client.PostAsync($"/api/orders/{orderId}/send-confirmation-email", content: null);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "an already-sent mail is a no-op, not an error");
        await SettleDetachedWorkAsync();

        VerifyGuestReceipts(orderId, Times.Once());
        VerifyAdminAlerts(orderId, Times.Once());
    }

    /// <summary>
    /// The claim is not a tombstone. A send that fails gives it back, or the first provider hiccup
    /// would make an order's mail permanently unsendable — a worse failure than the one being
    /// fixed. The client's own call is what redeems it here, exactly as it would in production.
    /// </summary>
    [Fact]
    public async Task A_failed_send_leaves_the_mail_still_sendable()
    {
        var attempts = 0;
        _email.Setup(e => e.SendOrderReceivedEmailAsync(
                It.IsAny<CultureInfo>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<IEnumerable<(string, int, decimal)>>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(() => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new InvalidOperationException("provider down"))
                : Task.CompletedTask);

        AuthenticateAsAnonymous();
        var orderId = await PlaceOrderAsync(OrderType.Takeaway, GuestEmail);

        await WaitUntilAsync(async () => (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived)).Count == 0);

        var resend = await Client.PostAsync($"/api/orders/{orderId}/send-confirmation-email", content: null);

        resend.StatusCode.Should().Be(HttpStatusCode.OK);
        VerifyGuestReceipts(orderId, Times.Exactly(2), "the first attempt failed, so the resend had to be allowed");
        (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived))
            .Single().SentAt.Should().NotBeNull();
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

        var orderId = await PlaceOrderAsync(OrderType.Takeaway, customerEmail: null);

        // The admin alert is the control: it is queued for this same order, so its arrival proves
        // the mail path ran at all and the absent receipt is a decision rather than a dead path.
        await WaitUntilAsync(async () =>
            (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderAdminAlert)).Count == 1);
        await SettleDetachedWorkAsync();

        VerifyGuestReceipts(orderId, Times.Never());
        VerifyAdminAlerts(orderId, Times.Once());
    }

    /// <summary>
    /// Dine-in takes the extra branch: it auto-confirms, so it gets the confirmed mail as well as
    /// the pair above — the behaviour that used to be inline in the handler.
    /// </summary>
    [Fact]
    public async Task A_dine_in_order_also_gets_its_confirmed_mail()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(OrderType.DineIn, GuestEmail);
        await WaitForMailsAsync(orderId);

        VerifyGuestReceipts(orderId, Times.Once());
        VerifyAdminAlerts(orderId, Times.Once());
        _email.Verify(e => e.SendOrderConfirmedEmailAsync(
            It.IsAny<CultureInfo>(), GuestEmail, It.IsAny<string>(), It.IsAny<string>(), nameof(OrderType.DineIn), It.IsAny<int>()),
            Times.Once());
    }

    /// <summary>
    /// The money path, and the one behaviour change worth pinning hardest: an order that declares
    /// an online payment is held <c>Pending</c> and owes nobody a confirmation until Stripe reports
    /// the money. It must send NOTHING at creation — the settle path mails it.
    /// </summary>
    [Fact]
    public async Task An_online_payment_order_sends_nothing_at_creation()
    {
        AuthenticateAsAnonymous();

        var orderId = await PlaceOrderAsync(OrderType.Takeaway, GuestEmail, PaymentMethod.OnlinePayment);
        await SettleDetachedWorkAsync();

        VerifyGuestReceipts(orderId, Times.Never());
        VerifyAdminAlerts(orderId, Times.Never());
        (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived)).Should().BeEmpty();
        (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderAdminAlert)).Should().BeEmpty();
    }

    // ---- Helpers -----------------------------------------------------------------------------

    /// <remarks>
    /// The culture is asserted CONCRETELY here, not with <c>It.IsAny</c>, and these two are the
    /// right place for it: both mails are dispatched from a detached task that resolves its own
    /// scope, which is exactly where an ambient <c>CurrentUICulture</c> would silently be the
    /// server's rather than the recipient's (EMAIL-LOCALISATION-PLAN §6.1). With `IsAny` everywhere
    /// the compiler's "an argument must be present" is the only thing holding S1's contract, and a
    /// future `null` or an ambient lookup would pass the whole suite. S4 will change the expected
    /// value here to the order's own language — that is the point.
    /// </remarks>
    private void VerifyGuestReceipts(Guid orderId, Times times, string? because = null) =>
        _email.Verify(e => e.SendOrderReceivedEmailAsync(
            EmailCultures.English, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<IEnumerable<(string, int, decimal)>>(), It.IsAny<string?>(), It.IsAny<string?>()),
            times, because ?? $"order {orderId}");

    /// <inheritdoc cref="VerifyGuestReceipts"/>
    private void VerifyAdminAlerts(Guid orderId, Times times) =>
        _email.Verify(e => e.SendOrderConfirmationAdminEmailAsync(
            EmailCultures.English, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<IEnumerable<(string, int, decimal)>>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            times, $"order {orderId}");

    private async Task<Guid> PlaceOrderAsync(
        OrderType type, string? customerEmail, PaymentMethod payment = PaymentMethod.Cash)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = type.ToString(),
            customerName = "Guest",
            customerEmail,
            tableNumber = type == OrderType.DineIn ? 1 : (int?)null,
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = payment.ToString(), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Orders.AsNoTracking().SingleAsync()).Id;
    }

    /// <summary>
    /// Both mails are dispatched on detached tasks by design — neither may delay or fail the
    /// request that places the order — so every assertion has to wait for them first.
    /// </summary>
    private Task WaitForMailsAsync(Guid orderId) => WaitUntilAsync(async () =>
        (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderReceived)).Any(c => c.SentAt != null)
        && (await ClaimsForAsync(orderId, OutboundEmailTypes.OrderAdminAlert)).Any(c => c.SentAt != null));

    /// <summary>
    /// Lets any detached mail task finish before the test ends. Without it a task can outlive the
    /// factory and race the next test's database reset — and a "sent nothing" assertion could pass
    /// merely by being early.
    /// </summary>
    private static Task SettleDetachedWorkAsync() => Task.Delay(500);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The awaited mail state never arrived.");
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
