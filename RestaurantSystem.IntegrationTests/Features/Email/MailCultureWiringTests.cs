using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Email;

/// <summary>
/// GAP-2 S5 (EMAIL-LOCALISATION-PLAN §5) — the slice where the language a guest expressed finally
/// reaches the mail about them. S4 froze it on the row; here every send path resolves it and passes
/// it to the templates.
/// </summary>
/// <remarks>
/// <para>
/// Asserted against a recording <see cref="IEmailService"/> and driven over HTTP with a real
/// <c>Accept-Language</c> header, because the whole chain — header, capture, column, resolver,
/// send — is what is under test. Nothing here asserts on rendered text: until S7 ships a
/// translation, French and English render the same neutral resources byte for byte, so the
/// argument IS the observable. That is also why every expectation below names a concrete
/// <see cref="CultureInfo"/> instead of <c>It.IsAny</c>.
/// </para>
/// <para>
/// The M14 leg below asserts English on an unconfigured host, where English is also the placeholder
/// S1 left behind — so it cannot fail on a revert, and it is kept only because it WOULD fail if
/// someone wired the guest's culture into the operator alert. The two-sided version, on a tenant
/// whose own language is German, is <c>OperatorMailCultureTests</c>.
/// </para>
/// <para>
/// The two send paths whose culture is NOT observable here are the reservation-rejected mail and
/// the reservation admin alert: both render the template themselves and hand
/// <c>SendEmailAsync(to, subject, html, text)</c> four strings. They are covered by
/// <c>EmailLanguageResolverExtensionsTests</c> at the resolver level, and become observable the
/// moment S7 gives two languages different words.
/// </para>
/// </remarks>
public class MailCultureWiringTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;
    private const string GuestEmail = "guest@example.com";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private readonly Mock<IEmailService> _email = new();
    private Guid _pizzaId;

    public MailCultureWiringTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Singleton for the same reason <c>ServerSideOrderMailTests</c> uses one: both order mails are
    /// dispatched from a detached task that resolves its own scope.
    /// </summary>
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);
    }

    /// <summary>
    /// The headline of the slice, and the trap of §1 in one test: the guest reads the receipt in
    /// the language they ordered in, and the restaurant reads its own alert about that same order
    /// in the tenant's language. A single resolved culture shared by both would be a regression the
    /// restaurant notices first.
    /// </summary>
    [Fact]
    public async Task An_order_mails_the_guest_in_their_language_and_the_restaurant_in_the_tenants()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("fr-CH,fr;q=0.9,en;q=0.8");

        var orderId = await PlaceOrderAsync(OrderType.Takeaway);
        await WaitForMailsAsync(orderId);

        _email.Verify(e => e.SendOrderReceivedEmailAsync(
            French, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<IEnumerable<(string, int, decimal)>>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once());

        _email.Verify(e => e.SendOrderConfirmationAdminEmailAsync(
            English, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<IEnumerable<(string, int, decimal)>>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once(),
            "the operator alert follows the tenant, never the diner");
    }

    /// <summary>
    /// A dine-in order auto-confirms, so the confirmed mail is sent on the creation path too — and
    /// it is sent from the same request, which makes it the one order mail an ambient-culture
    /// design would have got right by accident.
    /// </summary>
    [Fact]
    public async Task The_dine_in_confirmation_follows_the_order_as_well()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("de");

        var orderId = await PlaceOrderAsync(OrderType.DineIn);
        await WaitForMailsAsync(orderId);

        _email.Verify(e => e.SendOrderConfirmedEmailAsync(
            German, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), nameof(OrderType.DineIn), It.IsAny<int>()),
            Times.Once());
    }

    /// <summary>
    /// §6.10, the one that would have bitten. The guest ordered in French and closed the tab; the
    /// mail goes out later from a STAFF request in a restaurant browser set to German. An implicit
    /// rank 3 — or an ambient culture — would mail the diner in the till's language.
    /// </summary>
    [Fact]
    public async Task A_staff_status_change_mails_the_guest_in_the_orders_language_not_the_staff_browsers()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("fr");
        var orderId = await PlaceOrderAsync(OrderType.Takeaway);
        await WaitForMailsAsync(orderId);

        AuthenticateAsAdmin();
        WithAcceptLanguage("de-DE,de;q=0.9");

        var response = await Client.PutAsJsonAsync($"/api/orders/{orderId}/status", new
        {
            newStatus = OrderStatus.Confirmed.ToString(),
            estimatedPreparationMinutes = 20,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        _email.Verify(e => e.SendOrderConfirmedEmailAsync(
            French, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Once());
    }

    /// <summary>
    /// The same rule on the cancellation path, which is staff-only by design and refunds money as
    /// it goes — a guest reading "your order was cancelled" in a language they never chose is the
    /// worst mail in the product to get wrong.
    /// </summary>
    [Fact]
    public async Task A_cancellation_is_written_in_the_language_the_order_was_placed_in()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("fr");
        var orderId = await PlaceOrderAsync(OrderType.Takeaway);
        await WaitForMailsAsync(orderId);

        AuthenticateAsAdmin();
        WithAcceptLanguage("de");

        var response = await Client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel", new { cancellationReason = "out of dough" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        _email.Verify(e => e.SendOrderCancellationEmailAsync(
            French, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task A_reservation_confirmation_is_written_in_the_language_it_was_booked_in()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("nl-NL,nl;q=0.9");

        await BookTableAsync();

        _email.Verify(e => e.SendReservationConfirmationEmailAsync(
            Dutch, "ada@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<string?>()),
            Times.Once());
    }

    /// <summary>
    /// M12, and §6.10's flagship case: the approve button lives in the RESTAURANT's own alert mail
    /// and is clicked in the RESTAURANT's browser. The guest booked in Dutch and is not present;
    /// an implicit rank 3 would send them their table confirmation in German.
    /// </summary>
    [Fact]
    public async Task A_quick_approved_reservation_is_written_in_the_guests_language_not_the_clickers()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("nl-NL,nl;q=0.9");
        var reservationId = await BookTableAsync();

        AuthenticateAsAdmin();
        WithAcceptLanguage("de-DE,de;q=0.9");

        var response = await Client.PostAsync($"/api/reservations/{reservationId}/confirm", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        _email.Verify(e => e.SendReservationApprovedEmailAsync(
            Dutch, "ada@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once());
    }

    /// <summary>
    /// §1 rank 2 outranks rank 3 on an account's own mail, and this endpoint is why it matters:
    /// <c>/forgot-password</c> is anonymous and takes an address from anyone, so honouring the
    /// caller's header would let a stranger pick the language of a reset mail in someone else's
    /// inbox.
    /// </summary>
    [Fact]
    public async Task An_account_mail_follows_the_account_and_not_whoever_asked_for_it()
    {
        await SetProfileLanguageAsync(Guid.Parse(Common.TestAuthHandler.UserId), "de");
        var user = await UserAsync(Guid.Parse(Common.TestAuthHandler.UserId));

        AuthenticateAsAnonymous();
        WithAcceptLanguage("fr");

        var response = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        _email.Verify(e => e.SendPasswordResetEmailAsync(
            German, It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Once());
    }

    /// <summary>
    /// §6.1 in a test: an ambient culture is not a channel. Set the process culture to French, send
    /// a request that asks for nothing, and the mail must still be the tenant's English — otherwise
    /// production would mail every guest in whatever the container's locale happens to be.
    /// </summary>
    [Fact]
    public async Task The_ambient_culture_is_never_mistaken_for_the_recipients()
    {
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CH");

            AuthenticateAsAnonymous();
            Client.DefaultRequestHeaders.Remove("Accept-Language");

            var orderId = await PlaceOrderAsync(OrderType.Takeaway);
            await WaitForMailsAsync(orderId);

            _email.Verify(e => e.SendOrderReceivedEmailAsync(
                English, GuestEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<IEnumerable<(string, int, decimal)>>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private void WithAcceptLanguage(string header)
    {
        Client.DefaultRequestHeaders.Remove("Accept-Language");
        Client.DefaultRequestHeaders.Add("Accept-Language", header);
    }

    private async Task<Guid> PlaceOrderAsync(OrderType type)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = type.ToString(),
            customerName = "Guest",
            customerEmail = GuestEmail,
            tableNumber = type == OrderType.DineIn ? 1 : (int?)null,
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = PaymentMethod.Cash.ToString(), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<Guid> BookTableAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var table = new Table { TableNumber = "T-CULT", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
        context.Tables.Add(table);
        await context.SaveChangesAsync();

        var response = await Client.PostAsJsonAsync("/api/reservations", new
        {
            tableId = table.Id,
            customerName = "Ada Lovelace",
            customerEmail = "ada@example.com",
            customerPhone = "+41791112233",
            reservationDate = DateTime.UtcNow.AddDays(1).Date,
            startTime = "19:00:00",
            endTime = "21:00:00",
            numberOfGuests = 2,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    /// <summary>Both order mails are dispatched on detached tasks, so every assertion waits.</summary>
    private Task WaitForMailsAsync(Guid orderId) => WaitUntilAsync(async () =>
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claims = await context.OutboundEmails.AsNoTracking()
            .Where(e => e.EntityId == orderId)
            .ToListAsync();

        return claims.Count(c => c.SentAt != null) == 2;
    });

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

    private async Task<ApplicationUser> UserAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Users.AsNoTracking().FirstAsync(u => u.Id == id);
    }

    private async Task SetProfileLanguageAsync(Guid userId, string language)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users.FirstAsync(u => u.Id == userId);
        user.PreferredLanguage = language;
        await context.SaveChangesAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.AsNoTracking().FirstAsync(p => p.Name == "Test Pizza")).Id;
    }
}
