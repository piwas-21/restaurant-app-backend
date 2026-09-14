using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The order DISPLAY-currency contract (POS plan C18): the first tender carrying a currency
/// wins (ANY status), else the tenant's declared <see cref="RestaurantInfo.Currency"/>, else
/// null. Drives the real Postgres database through the same DI scope the queries use — the
/// tenant half of the resolution is a database read, so an in-memory double would prove nothing.
/// </summary>
/// <remarks>
/// Display metadata only: nothing here may move a price, a rounding rule or a charge. The write
/// surface (PUT /api/restaurant-info) is covered too, because the fallback leg is only as real
/// as the admin's ability to declare the value.
/// </remarks>
[Collection("Database Lane 3")]
public class OrderDisplayCurrencyTests : IntegrationTestBase
{
    public OrderDisplayCurrencyTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string TenderOrder = "CUR-A-TENDER";
    private const string CashLikeOrder = "CUR-B-CASHLIKE";
    private const string BareOrder = "CUR-C-BARE";
    private const string TwoTenderOrder = "CUR-D-TWOTENDERS";

    // ── Resolution: tender wins ──────────────────────────────────────────

    [Fact]
    public async Task Map_TenderCurrencyWins_OverTenantSetting_EvenOnANonCompletedTender()
    {
        await SetTenantCurrencyAsync("CHF");

        var dto = await MapOrderByNumberAsync(TenderOrder);

        // The tender sits at Processing ON PURPOSE: a capture still names the currency the
        // money moves in, and a status filter would relabel the order mid-payment.
        dto.Currency.Should().Be("eur");
        dto.Payments.Single().Status.Should().Be(nameof(PaymentStatus.Processing));
    }

    [Fact]
    public async Task Map_EarliestTenderWins_WhenSeveralCarryDifferentCurrencies()
    {
        var dto = await MapOrderByNumberAsync(TwoTenderOrder);

        // chf is the EARLIER tender (Pending), eur the later Completed one — "first payment
        // record" must be deterministic, or the label flips with row order.
        dto.Currency.Should().Be("chf");
    }

    // ── Resolution: tenant fallback ──────────────────────────────────────

    [Fact]
    public async Task Map_TenantCurrencyUsed_WhenNoTenderCarriesOne()
    {
        await SetTenantCurrencyAsync("CHF");

        // A cash-like tender (no currency of its own) and a bare order both fall through.
        (await MapOrderByNumberAsync(CashLikeOrder)).Currency.Should().Be("CHF");
        (await MapOrderByNumberAsync(BareOrder)).Currency.Should().Be("CHF");
    }

    [Fact]
    public async Task Map_Null_WhenNeitherTenderNorTenantDeclares()
    {
        await SetTenantCurrencyAsync(null);

        (await MapOrderByNumberAsync(CashLikeOrder)).Currency.Should().BeNull();
        (await MapOrderByNumberAsync(BareOrder)).Currency.Should().BeNull();
    }

    // ── Resolution: printer feed (the polled wire surface) ───────────────

    [Fact]
    public async Task PrinterFeed_ReturnsResolvedCurrency_PerOrder()
    {
        // The RestaurantInfo singleton is in Respawn's TablesToIgnore, so a currency set by a
        // sibling test survives the per-test reset — every test here states its OWN state.
        await SetTenantCurrencyAsync(null);
        AuthenticateAsDevice();

        // No tenant currency declared yet: the settled tenders carry their own label, and the
        // orders with nothing to say must render an explicit null KEY — a consumer has to be
        // able to tell "no label" from "field missing", which is the additive-contract point.
        var undeclared = JsonNode.Parse(await Client.GetStringAsync("/api/orders/printer-feed"))!;
        var items = undeclared["data"]!["items"]!.AsArray();
        CurrencyOf(items, TenderOrder).Should().Be("eur");
        CurrencyOf(items, TwoTenderOrder).Should().Be("chf");

        var bare = items.Single(o => o!["orderNumber"]!.GetValue<string>() == BareOrder)!.AsObject();
        bare.ContainsKey("currency").Should().BeTrue("the key must ship even when the value is null");
        // A JSON-null property reads back as a null JsonNode — that null node IS the assertion.
        bare["currency"].Should().BeNull("null is the undeclared state, shipped as an explicit key");

        // Declaring a tenant currency relabels ONLY the orders whose tenders carry none —
        // the settled EUR/CHF tenders keep their own, on the same wire.
        await SetTenantCurrencyAsync("CHF");
        var declared = JsonNode.Parse(await Client.GetStringAsync("/api/orders/printer-feed"))!;
        var declaredItems = declared["data"]!["items"]!.AsArray();
        CurrencyOf(declaredItems, TenderOrder).Should().Be("eur");
        CurrencyOf(declaredItems, CashLikeOrder).Should().Be("CHF");
        CurrencyOf(declaredItems, TwoTenderOrder).Should().Be("chf");
    }

    // ── The declaring surface: PUT /api/restaurant-info ──────────────────

    [Fact]
    public async Task RestaurantInfoUpdate_RoundTripsCurrency_AndClearsOnNull()
    {
        AuthenticateAsAdmin();

        var set = await Client.PutAsJsonAsync("/api/restaurant-info", Form("EUR"));
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCurrencyAsync(set)).Should().Be("EUR");

        // Full-replace semantics: a PUT without the field CLEARS it, exactly like
        // ThemePaletteKey — the form always sends the whole settings state.
        var clear = await Client.PutAsJsonAsync("/api/restaurant-info", Form(null));
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCurrencyAsync(clear)).Should().BeNull();
    }

    [Fact]
    public async Task RestaurantInfoUpdate_RejectsAThreeLetterNonIsoCurrency()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync("/api/restaurant-info", Form("euro"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Fixture + helpers ────────────────────────────────────────────────

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        // Orders first and SAVED: the tender helpers resolve the order id with a database
        // query, which cannot see an added-but-unsaved row.
        context.Add(NewConfirmedOrder(TenderOrder, now));
        context.Add(NewConfirmedOrder(CashLikeOrder, now.AddMinutes(11)));
        context.Add(NewConfirmedOrder(BareOrder, now.AddMinutes(12)));
        context.Add(NewConfirmedOrder(TwoTenderOrder, now));
        await context.SaveChangesAsync();

        SeedTender(context, TenderOrder, "eur", PaymentStatus.Processing, now.AddMinutes(10));
        SeedTender(context, CashLikeOrder, null, PaymentStatus.Pending, now.AddMinutes(11));
        SeedTwoTenders(context, TwoTenderOrder, now);
        await context.SaveChangesAsync();
    }

    private static Order NewConfirmedOrder(string orderNumber, DateTime orderDate)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10.00m,
            Total = 10.00m,
            OrderDate = orderDate,
            CreatedAt = orderDate,
            CreatedBy = "test",
        };
        // Deliberately NO items: OrderItems.product_id is a real FK and item rendering is not
        // what this class pins — the currency contract rides on the order + its tenders alone.
        return order;
    }

    private static void SeedTender(
        ApplicationDbContext context, string orderNumber, string? currency,
        PaymentStatus status, DateTime paidAt)
    {
        var orderId = context.Orders.Single(o => o.OrderNumber == orderNumber).Id;
        context.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            PaymentMethod = currency is null ? PaymentMethod.Cash : PaymentMethod.CreditCard,
            Amount = 10.00m,
            Status = status,
            Currency = currency,
            PaymentDate = paidAt,
            CreatedAt = paidAt,
            CreatedBy = "test",
        });
    }

    private static void SeedTwoTenders(ApplicationDbContext context, string orderNumber, DateTime now)
    {
        var orderId = context.Orders.Single(o => o.OrderNumber == orderNumber).Id;
        context.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 4.00m,
            Status = PaymentStatus.Pending,
            Currency = "chf",
            PaymentDate = now.AddMinutes(20),
            CreatedAt = now.AddMinutes(20),
            CreatedBy = "test",
        });
        context.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            PaymentMethod = PaymentMethod.CreditCard,
            Amount = 6.00m,
            Status = PaymentStatus.Completed,
            Currency = "eur",
            PaymentDate = now.AddMinutes(21),
            CreatedAt = now.AddMinutes(21),
            CreatedBy = "test",
        });
    }

    private async Task SetTenantCurrencyAsync(string? currency)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var info = await db.RestaurantInfo.SingleAsync();
        info.Currency = currency;
        await db.SaveChangesAsync();
    }

    private async Task<OrderDto> MapOrderByNumberAsync(string orderNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Fresh, untracked, payments included — the exact shape GetOrdersQuery and the printer
        // feed hand the mapper, so the test cannot pass on fixup luck.
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.OrderNumber == orderNumber);
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();
        return mapper.MapToOrderDto(order);
    }

    private static string? CurrencyOf(JsonArray items, string orderNumber) =>
        items.Single(o => o!["orderNumber"]!.GetValue<string>() == orderNumber)!["currency"]!
            .GetValue<string?>();

    // A full valid settings form: the PUT is full-replace, so the required fields must ride
    // along on every call or the mutation is refused before currency is even looked at.
    private static UpdateRestaurantInfoCommand Form(string? currency) => new(
        Name: "Currency Fixture Restaurant",
        AddressLine1: "Rue du Test 1",
        AddressLine2: null,
        City: "Genève",
        PostalCode: "1202",
        Country: "Switzerland",
        Latitude: null,
        Longitude: null,
        Email: "currency-fixture@example.com",
        Website: null,
        ThemePaletteKey: null,
        MenuLayout: "tabs",
        ShowMenuBundlesOnAllTab: false,
        Currency: currency);

    private static async Task<string?> ReadCurrencyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(body)!;
        // Defensive on purpose: a `!` on a missing node dies as a bare NRE, which says nothing
        // about WHERE the contract broke. Surface the whole envelope instead.
        var data = root["data"] ?? throw new Xunit.Sdk.XunitException($"no `data` in envelope: {body}");
        return data["currency"]?.GetValue<string?>();
    }
}
