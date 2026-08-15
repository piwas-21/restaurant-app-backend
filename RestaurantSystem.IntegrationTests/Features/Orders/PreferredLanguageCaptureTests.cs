using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// GAP-2 S4 — the first slice where a guest's own language becomes a stored fact
/// (EMAIL-LOCALISATION-PLAN §2, §5 S4). Everything before this was plumbing: S1 threaded a culture
/// through the templates, S2 added the columns, S3 added the chain. Here the write paths use them.
/// </summary>
/// <remarks>
/// Driven through HTTP with a real <c>Accept-Language</c> header rather than by calling the
/// handlers: the header is the entire capture channel (§2 chose it over a body field precisely so
/// no DTO changes), so a test that set the value directly would prove nothing about whether the
/// header reaches the row.
/// </remarks>
public class PreferredLanguageCaptureTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;

    private Guid _pizzaId;

    public PreferredLanguageCaptureTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Theory]
    [InlineData("fr-CH", "fr")]
    [InlineData("fr-CH,fr;q=0.9,en;q=0.8", "fr")]   // a real browser's header, not just a tag
    [InlineData("de", "de")]
    public async Task An_order_freezes_the_language_the_guest_ordered_in(string header, string expected)
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage(header);

        var orderId = await PlaceOrderAsync();

        (await OrderAsync(orderId)).PreferredLanguage.Should().Be(expected);
    }

    [Fact]
    public async Task A_reservation_freezes_it_too()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("nl-NL,nl;q=0.9");

        var reservationId = await BookTableAsync();

        (await ReservationAsync(reservationId)).PreferredLanguage.Should().Be("nl");
    }

    /// <summary>
    /// No header is not an error and not an empty column: an order must always carry a language,
    /// because the mails about it are sent from paths that have no request to ask (§1 rank 4). The
    /// test host is unconfigured, so the tenant default is <c>en</c>.
    /// </summary>
    [Fact]
    public async Task With_no_header_at_all_an_order_still_carries_the_tenant_language()
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Remove("Accept-Language");

        var orderId = await PlaceOrderAsync();

        (await OrderAsync(orderId)).PreferredLanguage.Should().Be("en");
    }

    /// <summary>
    /// §1 rank 2 outranks rank 3, and this is the case it exists for: someone who set their
    /// language in the app is not asking to be mailed in whatever language the machine they happen
    /// to be sitting at reports.
    /// </summary>
    [Fact]
    public async Task An_accounts_own_preference_beats_the_browsers()
    {
        await SetProfileLanguageAsync(Guid.Parse(TestAuthHandler.UserId), "fr");

        AuthenticateAsUser();
        WithAcceptLanguage("de-DE,de;q=0.9");

        var orderId = await PlaceOrderAsync();

        (await OrderAsync(orderId)).PreferredLanguage.Should().Be("fr");
    }

    /// <summary>
    /// The order a WAITER types in is not the guest's request, and on that path the user id is the
    /// staff account. Taking the staff browser's language — or the staff member's own stored
    /// preference — would freeze the restaurant's UI language onto the guest's row and then mail
    /// the guest in it. Found in review; the failure §1 forbids for the operator alerts, pointing
    /// the other way.
    /// </summary>
    [Fact]
    public async Task A_staff_entered_order_does_not_inherit_the_staff_browsers_language()
    {
        await SetProfileLanguageAsync(Guid.Parse(TestAuthHandler.AdminUserId), "tr");

        AuthenticateAsAdmin();
        WithAcceptLanguage("de-DE,de;q=0.9");

        var orderId = await PlaceOrderAsync();

        (await OrderAsync(orderId)).PreferredLanguage.Should().Be("en",
            "neither the staff browser nor the staff member's own preference is the guest's");
    }

    /// <summary>
    /// The one branch the S4 refactor moved that changes a response: a delivery order with no
    /// resolvable address. Status, message and the absence of an order row all have to survive the
    /// extraction into IOrderFactory. (The factory returns before anything is added to the change
    /// tracker, so the empty table is not by itself proof that the transaction rolled back — it is
    /// proof that the early return still happens where it did.)
    /// </summary>
    [Fact]
    public async Task A_delivery_order_with_no_address_still_fails_the_same_way()
    {
        AuthenticateAsAnonymous();

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = OrderType.Delivery.ToString(),
            customerName = "Guest",
            customerEmail = "guest@example.com",
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = PaymentMethod.Cash.ToString(), amount = PizzaPrice } },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the API answers failures in the envelope");
        body.Should().Contain("Delivery address is required for delivery orders");
        body.Should().Contain("\"success\":false");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Orders.AsNoTracking().AnyAsync()).Should().BeFalse("no order was created");
    }

    /// <summary>
    /// A language the tenant does not sell in is absent, not an error — the guest still gets an
    /// order, and the mail falls through to the tenant default rather than failing.
    /// </summary>
    [Fact]
    public async Task A_language_nobody_supports_falls_through_instead_of_failing()
    {
        AuthenticateAsAnonymous();
        WithAcceptLanguage("klingon");

        var orderId = await PlaceOrderAsync();

        (await OrderAsync(orderId)).PreferredLanguage.Should().Be("en");
    }

    /// <summary>
    /// Registration records only what the person actually expressed. Unlike an order, an account
    /// with no language is a legitimate state — §1 rank 2 is then simply empty and later mail falls
    /// through — so an absent header must not invent a preference the user never set.
    /// </summary>
    [Fact]
    public async Task Registering_records_the_language_asked_for_and_nothing_when_none_is()
    {
        (await RegisterAsync("with-language@example.com", "it")).Should().Be("it");
        (await RegisterAsync("no-language@example.com", header: null)).Should().BeNull(
            "an account records a preference, never a guess");
    }

    /// <summary>
    /// The profile write is a deliberate choice, so an unsupported value is a 400 rather than a
    /// silently ignored one — a setting that does not stick and does not complain is the worse of
    /// the two failures. An absent field must leave the stored preference alone: no client sends
    /// it until S6.
    /// </summary>
    [Fact]
    public async Task The_profile_endpoint_sets_it_validates_it_and_leaves_it_alone_when_absent()
    {
        AuthenticateAsUser();
        var userId = Guid.Parse(TestAuthHandler.UserId);

        (await UpdateProfileAsync("tr")).Should().Be(HttpStatusCode.OK);
        (await UserAsync(userId)).PreferredLanguage.Should().Be("tr");

        (await UpdateProfileAsync("klingon")).Should().Be(HttpStatusCode.BadRequest);
        (await UserAsync(userId)).PreferredLanguage.Should().Be("tr", "a rejected write changes nothing");

        // Three shapes of "the client said nothing": the field missing entirely (every client
        // until S6), an explicit null, and the empty string a cleared <select> posts. None may
        // wipe a preference the user set — but note that also means there is currently no way to
        // go back to "no preference"; S6 owns that decision.
        (await UpdateProfileAsync(language: null)).Should().Be(HttpStatusCode.OK);
        (await UserAsync(userId)).PreferredLanguage.Should().Be("tr", "an absent field means unchanged");

        (await UpdateProfileAsync(language: null, includeKey: true)).Should().Be(HttpStatusCode.OK);
        (await UserAsync(userId)).PreferredLanguage.Should().Be("tr", "an explicit null means unchanged");

        (await UpdateProfileAsync("")).Should().Be(HttpStatusCode.OK);
        (await UserAsync(userId)).PreferredLanguage.Should().Be("tr", "an empty string means unchanged");
    }

    private void WithAcceptLanguage(string header)
    {
        Client.DefaultRequestHeaders.Remove("Accept-Language");
        Client.DefaultRequestHeaders.Add("Accept-Language", header);
    }

    private async Task<Guid> PlaceOrderAsync(OrderType type = OrderType.Takeaway, object? deliveryAddress = null)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = type.ToString(),
            customerName = "Guest",
            customerEmail = "guest@example.com",
            tableNumber = type == OrderType.DineIn ? 1 : (int?)null,
            deliveryAddress,
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = PaymentMethod.Cash.ToString(), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The id comes from the response, not from "the newest row": CreatedAt is assigned in
        // process and two orders in the same millisecond would make this test pick by luck.
        return (await ReadIdAsync(response))!.Value;
    }

    private static async Task<Guid?> ReadIdAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return payload.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("id", out var id)
                ? id.GetGuid()
                : null;
    }

    private async Task<Guid> BookTableAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Seeded here rather than assumed: the basic seed carries no table, and a test that
        // silently found none would fail on the fixture instead of on the behaviour.
        var table = new Table { TableNumber = "T-LANG", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
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

        return (await ReadIdAsync(response))!.Value;
    }

    private async Task<string?> RegisterAsync(string email, string? header)
    {
        AuthenticateAsAnonymous();

        if (header is null)
        {
            Client.DefaultRequestHeaders.Remove("Accept-Language");
        }
        else
        {
            WithAcceptLanguage(header);
        }

        var response = await Client.PostAsJsonAsync("/api/User/register/customer", new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            email,
            password = "Str0ng!Passw0rd",          // pragma: allowlist secret
            confirmPassword = "Str0ng!Passw0rd",   // pragma: allowlist secret
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Users.AsNoTracking().FirstAsync(u => u.Email == email)).PreferredLanguage;
    }

    private async Task<HttpStatusCode> UpdateProfileAsync(string? language, bool includeKey = false)
    {
        var body = language is null && !includeKey
            ? (object)new { firstName = "Ada", lastName = "Lovelace" }
            : new { firstName = "Ada", lastName = "Lovelace", preferredLanguage = language };

        return (await Client.PutAsJsonAsync("/api/User/profile", body)).StatusCode;
    }

    private async Task<Order> OrderAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == id);
    }

    private async Task<Reservation> ReservationAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Reservations.AsNoTracking().FirstAsync(r => r.Id == id);
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
