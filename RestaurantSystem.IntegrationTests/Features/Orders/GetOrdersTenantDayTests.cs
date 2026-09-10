using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The orders list answers on the RESTAURANT'S day when the caller names one: <c>?tenantDay=D</c>
/// covers the venue-day window [D 00:00, D+1 00:00) on the tenant clock (the till-day rule of
/// <c>GetZReportQuery</c>, backend #372) and REPLACES the raw UTC bounds — the backend half of
/// cashier POS C09 (frontend #545).
/// </summary>
/// <remarks>
/// Seeded instants are chosen so UTC and the venue's wall clock disagree about which DAY an order
/// belongs to, and every assertion names rows, not counts. Everything runs over REAL HTTP because
/// the parameter's ISO binding (<c>?tenantDay=2026-05-02</c>) is part of the contract.
/// </remarks>
[Collection("Database Lane 1")]
public class GetOrdersTenantDayTests : IntegrationTestBase
{
    /// <summary>The venue's clock under test. Tests that need a different zone move the field first.</summary>
    private FixedTenantClock _clock = new("Europe/Zurich");

    public GetOrdersTenantDayTests(DatabaseFixture databaseFixture) : base(databaseFixture)
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
    /// already intact, skip the reset" path: the assertions below name exact row sets, and a
    /// sibling class in this lane that left orders behind would decide them instead.
    /// </summary>
    protected override Task SeedTestData() => base.SeedTestData();

    /// <summary>
    /// The defect on the money surface, west of UTC. New York in May is EDT (UTC-4), so venue day
    /// 2026-05-02 runs 04:00Z to the next 04:00Z: the 23:30 order is 03:30Z on the THIRD, and the
    /// naive UTC-day window answers with the previous evening's 23:30 instead — both wrong, in
    /// opposite directions, which is why one order alone would not pin this.
    /// </summary>
    [Fact]
    public async Task An_order_created_at_2330_venue_time_is_found_by_its_venue_day_not_the_UTC_day()
    {
        _clock = new FixedTenantClock("America/New_York");

        await SeedOrdersAsync(
            ("GOTD-EDGE-BEFORE", "2026-05-02T03:59:59Z"), // 23:59:59 venue on the 1st
            ("GOTD-EDGE-START", "2026-05-02T04:00:00Z"), // 00:00:00 venue on the 2nd, to the tick
            ("GOTD-MORNING", "2026-05-02T06:00:00Z"), // 02:00 venue on the 2nd
            ("GOTD-MONEY-2330", "2026-05-03T03:30:00Z"), // 23:30 venue on the 2nd
            ("GOTD-PREV-EVE", "2026-05-02T03:30:00Z"), // 23:30 venue on the 1st
            ("GOTD-NEXT-DAY", "2026-05-03T04:30:00Z")); // 00:30 venue on the 3rd

        var venueDay = await GetOrders("/api/orders?tenantDay=2026-05-02&pageSize=50");

        venueDay!.Items.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("GOTD-EDGE-START", "GOTD-MORNING", "GOTD-MONEY-2330");

        // The naive UTC-day window answers a DIFFERENT pair of the same instants — the fixture
        // discriminates, so a window rebuilt on UTC's day cannot pass the assertion above.
        var naiveUtcDay = await GetOrders(
            "/api/orders?startDate=2026-05-02T00:00:00Z&endDate=2026-05-02T23:59:59Z&pageSize=50");

        naiveUtcDay!.Items.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("GOTD-EDGE-BEFORE", "GOTD-EDGE-START", "GOTD-MORNING", "GOTD-PREV-EVE");

        // The window's end is the venue's NEXT midnight, not 24h from a misderived start: the
        // 00:30 order belongs to the 3rd and only to the 3rd.
        var nextVenueDay = await GetOrders("/api/orders?tenantDay=2026-05-03&pageSize=50");

        nextVenueDay!.Items.Select(o => o.OrderNumber).Should().BeEquivalentTo("GOTD-NEXT-DAY");
    }

    /// <summary>
    /// The 23-hour day. Zurich springs forward at 01:00 local on 2026-03-29, so venue day 29 runs
    /// 23:00Z (28th) to 22:00Z (29th) and the 30th starts at 22:00Z: the 00:30 order belongs to
    /// the 30th, while a window built as <c>start.AddDays(1)</c> (ends 23:00Z) would keep it on
    /// the 29th as well.
    /// </summary>
    [Fact]
    public async Task A_DST_transition_moves_the_venue_day_with_it()
    {
        await SeedOrdersAsync(
            ("GOTD-DST-MORNING", "2026-03-29T00:30:00Z"), // 01:30 CET on the 29th, before the jump
            ("GOTD-DST-LATE", "2026-03-29T21:30:00Z"), // 23:30 CEST on the 29th
            ("GOTD-DST-AFTER", "2026-03-29T22:30:00Z")); // 00:30 CEST on the 30th

        var the29th = await GetOrders("/api/orders?tenantDay=2026-03-29&pageSize=50");

        the29th!.Items.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("GOTD-DST-MORNING", "GOTD-DST-LATE");

        var the30th = await GetOrders("/api/orders?tenantDay=2026-03-30&pageSize=50");

        the30th!.Items.Select(o => o.OrderNumber).Should().BeEquivalentTo("GOTD-DST-AFTER");
    }

    /// <summary>
    /// REPLACES, not intersects: bounds that exclude the whole venue day must be ignored while
    /// <c>tenantDay</c> is present — and, alone, those same bounds must return nothing, so the
    /// non-empty result above is attributable to the replacement and not to weak bounds.
    /// </summary>
    [Fact]
    public async Task TenantDay_Replaces_StartDate_and_EndDate_rather_than_intersecting()
    {
        _clock = new FixedTenantClock("America/New_York");

        await SeedOrdersAsync(
            ("GOTD-RPL-EVE", "2026-05-03T03:30:00Z"), // 23:30 venue on the 2nd
            ("GOTD-RPL-NOON", "2026-05-02T16:30:00Z")); // 12:30 venue on the 2nd

        var replaced = await GetOrders(
            "/api/orders?tenantDay=2026-05-02&startDate=2020-01-01&endDate=2020-01-02&pageSize=50");

        replaced!.Items.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("GOTD-RPL-EVE", "GOTD-RPL-NOON");

        var boundsAlone = await GetOrders(
            "/api/orders?startDate=2020-01-01&endDate=2020-01-02&pageSize=50");

        boundsAlone!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// The venue day composes with the independent filters instead of bypassing them: search
    /// narrows the day to the matching rows and pagination pages them on the venue-day order.
    /// </summary>
    [Fact]
    public async Task TenantDay_composes_with_search_and_pagination()
    {
        await SeedOrdersAsync(
            ("GOTD-C-OLDEST", "2026-06-10T09:00:00Z"), // 11:00 venue on the 10th
            ("GOTD-C-MIDDLE", "2026-06-10T10:00:00Z"),
            ("GOTD-C-NEWEST", "2026-06-10T11:00:00Z"),
            ("GOTD-NOMATCH", "2026-06-10T12:00:00Z"), // in the day, fails the search
            ("GOTD-C-OUTSIDE", "2026-06-09T12:00:00Z")); // 14:00 venue on the 9th, fails the day

        var page1 = await GetOrders(
            "/api/orders?tenantDay=2026-06-10&search=GOTD-C&pageSize=2&page=1");

        page1!.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.Items.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("GOTD-C-NEWEST", "GOTD-C-MIDDLE"); // OrderDate desc

        var page2 = await GetOrders(
            "/api/orders?tenantDay=2026-06-10&search=GOTD-C&pageSize=2&page=2");

        page2!.TotalCount.Should().Be(3);
        page2.Items.Select(o => o.OrderNumber).Should().BeEquivalentTo("GOTD-C-OLDEST");
    }

    private async Task SeedOrdersAsync(params (string Number, string InstantUtc)[] orders)
    {
        await using var seed = DatabaseFixture.CreateContext();

        foreach (var (number, instant) in orders)
        {
            seed.Orders.Add(new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = number,
                Type = OrderType.Takeaway,
                Status = OrderStatus.Completed,
                PaymentStatus = PaymentStatus.Completed,
                SubTotal = 10m,
                Total = 10m,
                OrderDate = Utc(instant),
                CreatedAt = Utc(instant),
                CreatedBy = nameof(GetOrdersTenantDayTests),
                IsDeleted = false,
            });
        }

        await seed.SaveChangesAsync();
    }

    private static DateTime Utc(string instant) =>
        DateTime.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>GET /api/orders with the given query string; asserts OK and unwraps the envelope.</summary>
    private async Task<PagedResult<OrderDto>?> GetOrders(string url)
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        var envelope = JsonSerializer.Deserialize<ApiResponse<PagedResult<OrderDto>>>(body, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        return envelope.Data;
    }
}
