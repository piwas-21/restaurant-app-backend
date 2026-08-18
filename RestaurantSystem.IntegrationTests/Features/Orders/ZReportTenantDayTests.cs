using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The till closes on the RESTAURANT'S day, not UTC's (backend #372). Two halves, pinned
/// separately because fixing either alone still reports the wrong window:
/// the day the report COVERS (the handler) and the day it defaults TO (the controller).
/// </summary>
/// <remarks>
/// Every seeded instant below is chosen so UTC and the tenant's wall clock disagree about which
/// DAY it belongs to, and the amounts are distinct so an assertion names one order rather than a
/// count that several fixtures could satisfy.
/// </remarks>
[Collection("Database Lane 2")]
public class ZReportTenantDayTests : IntegrationTestBase
{
    /// <summary>The tenant's day under test. Zurich is +02:00 in May, so it is 22:00Z to 22:00Z.</summary>
    private static readonly DateOnly BusinessDay = new(2026, 5, 2);

    /// <summary>
    /// The clock the HOST runs on. Instance state, because the default-day test moves it to a zone
    /// picked from the real UTC hour — a literal zone cannot tell "the tenant's day" apart from
    /// "UTC's day" on a CI runner whose clock is anywhere at all.
    /// </summary>
    private FixedTenantClock _clock = new("Europe/Zurich");

    public ZReportTenantDayTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Transient, not singleton as production registers it: the field is read at every
        // resolution, so a test that moves the clock before calling is served the clock it set.
        services.AddTransient<ITenantClock>(_ => _clock);
    }

    /// <summary>
    /// Overridden — with the base behaviour — purely to opt this class out of the shared "seed is
    /// already intact, skip the reset" path: these assertions count ORDERS, and a sibling class in
    /// this lane that left some behind would make them pass or fail for reasons of its own.
    /// </summary>
    protected override Task SeedTestData() => base.SeedTestData();

    /// <summary>
    /// The defect on the money surface. 00:30 local on the 2nd is 22:30Z on the FIRST, and 01:30
    /// local on the 3rd is 23:30Z on the second: under the old UTC window the first order fell into
    /// the previous report and the second into this one — both wrong, and in opposite directions,
    /// which is why one order alone would not pin this.
    /// </summary>
    [Fact]
    public async Task The_report_covers_the_tenant_local_day_not_the_UTC_day()
    {
        await SeedOrdersAsync(
            ("ZR-TZ-AFTER-MIDNIGHT", "2026-05-01T22:30:00Z", 30m),
            ("ZR-TZ-LUNCH", "2026-05-02T12:00:00Z", 100m),
            ("ZR-TZ-NEXT-DAY", "2026-05-02T23:30:00Z", 7m),
            ("ZR-TZ-PREVIOUS-DAY", "2026-05-01T21:30:00Z", 500m));

        var report = await RunHandlerAsync(BusinessDay);

        report.TotalTransactions.Should().Be(2);
        report.GrossSales.Should().Be(
            130m,
            "the 00:30 order belongs to this day and the 01:30-next-morning one does not");
    }

    /// <summary>
    /// The window's edges, to the tick, on a day whose local midnight is not UTC's: 22:00Z is
    /// 00:00 in Zurich and is IN; one tick earlier is the previous business day.
    /// </summary>
    [Fact]
    public async Task The_window_is_half_open_on_the_tenant_midnight()
    {
        var startUtc = DateTime.Parse(
            "2026-05-01T22:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.Add(BuildOrder("ZR-TZ-EDGE-BEFORE", startUtc.AddTicks(-1), 11m));
            seed.Orders.Add(BuildOrder("ZR-TZ-EDGE-START", startUtc, 22m));
            seed.Orders.Add(BuildOrder("ZR-TZ-EDGE-END", startUtc.AddDays(1), 44m));
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync(BusinessDay);

        report.TotalTransactions.Should().Be(1);
        report.GrossSales.Should().Be(22m, "only the order at the tenant's own midnight is inside [start, end)");
    }

    /// <summary>
    /// The 23-hour day. 2026-03-29 runs 23:00Z (28th) to 22:00Z: an order at 22:30Z that evening is
    /// already 00:30 on the 30th and belongs to the NEXT till report — which a window built as
    /// <c>start.AddDays(1)</c> would count here as well as there.
    /// </summary>
    [Fact]
    public async Task A_DST_transition_moves_the_end_of_the_day_with_it()
    {
        await SeedOrdersAsync(
            ("ZR-DST-OPEN", "2026-03-28T23:00:00Z", 60m),
            ("ZR-DST-LATE", "2026-03-29T21:30:00Z", 9m),
            ("ZR-DST-TOMORROW", "2026-03-29T22:30:00Z", 300m));

        var report = await RunHandlerAsync(new DateOnly(2026, 3, 29));

        report.TotalTransactions.Should().Be(2);
        report.GrossSales.Should().Be(69m, "the local day ends at 22:00Z when the clocks have gone forward");
    }

    /// <summary>
    /// The report still NAMES the calendar day it was asked for. The cashier UI renders this as a
    /// date, so handing it the window start (22:00Z the evening before) would print the day before
    /// in any browser at or west of UTC — the same class of defect one layer up.
    /// </summary>
    [Fact]
    public async Task The_report_is_labelled_with_the_day_that_was_asked_for()
    {
        var report = await RunHandlerAsync(BusinessDay);

        report.ReportDate.Should().Be(new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// The controller's half, and the one the cashier meets at 00:30: omitting <c>date</c> must ask
    /// for the tenant's today.
    /// <para>
    /// Anchored to the REAL instant deliberately — no literal date can tell <c>_clock.Now.Date</c>
    /// apart from <c>DateTime.UtcNow.Date</c>, and a CI runner's clock is wherever it is. So the
    /// zone is picked from the current UTC hour such that the tenant's CALENDAR DAY is never UTC's,
    /// and that premise is asserted rather than assumed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_default_day_is_the_tenant_today_not_the_UTC_today()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var utcToday = DateOnly.FromDateTime(utcNow.UtcDateTime);

        // POSIX sign convention: Etc/GMT+12 is UTC-12 and Etc/GMT-12 is UTC+12. Both ids were
        // confirmed present in mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0.
        var zoneId = utcNow.Hour < 12 ? "Etc/GMT+12" : "Etc/GMT-12";
        _clock = new FixedTenantClock(zoneId);

        var tenantToday = DateOnly.FromDateTime(_clock.Now.Date);
        tenantToday.Should().NotBe(utcToday, "otherwise this test would pass against a UTC default too");

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/Orders/z-report");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var payload = JsonDocument.Parse(body);
        var reportDate = payload.RootElement.GetProperty("data").GetProperty("reportDate").GetString()!;

        DateOnly.Parse(reportDate[..10], CultureInfo.InvariantCulture)
            .Should().Be(tenantToday, "the till's today is the restaurant's, not the server's");
    }

    private async Task<ZReportDto> RunHandlerAsync(DateOnly date)
    {
        await using var context = DatabaseFixture.CreateContext();
        var handler = new GetZReportQueryHandler(context, _clock, NullLogger<GetZReportQueryHandler>.Instance);

        var response = await handler.Handle(new GetZReportQuery(date), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        return response.Data!;
    }

    private async Task SeedOrdersAsync(params (string Number, string InstantUtc, decimal Amount)[] orders)
    {
        await using var seed = DatabaseFixture.CreateContext();

        foreach (var (number, instant, amount) in orders)
        {
            seed.Orders.Add(BuildOrder(
                number,
                DateTime.Parse(
                    instant,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                amount));
        }

        await seed.SaveChangesAsync();
    }

    private static Order BuildOrder(string orderNumber, DateTime orderDateUtc, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Completed,
        PaymentStatus = PaymentStatus.Completed,
        SubTotal = amount,
        Total = amount,
        OrderDate = DateTime.SpecifyKind(orderDateUtc, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(ZReportTenantDayTests),
        IsDeleted = false,
    };
}
