using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Tenant;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

/// <summary>
/// <c>GET /api/tenant/today</c> — the day the restaurant is on, for clients that cannot work it
/// out (frontend #511 / #517). Pure unit tests: the zone is configuration and the instant is the
/// double's, so nothing here needs a host or a database.
/// </summary>
public class TenantTodayTests
{
    private static (TenantTodayDto Dto, HttpContext Http) Ask(string zoneId, DateTimeOffset instant)
    {
        var http = new DefaultHttpContext();
        var controller = new TenantTimeController(new FixedTenantClock(zoneId, instant))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var body = controller.GetToday().Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<TenantTodayDto>>().Subject;

        body.Success.Should().BeTrue();
        return (body.Data!, http);
    }

    /// <summary>
    /// The window the whole defect family lives in: 22:30Z on the 18th is already 00:30 on the
    /// 19th in Geneva. A client reading its own clock — or the server reading UTC — names the 18th,
    /// which is how the till read yesterday's takings (#372) and how the reservation form offers a
    /// day the booking endpoint then refuses as past (#369).
    /// </summary>
    [Fact]
    public void After_local_midnight_the_day_is_the_TENANTS_not_UTCs()
    {
        var (dto, _) = Ask("Europe/Zurich", new DateTimeOffset(2026, 8, 18, 22, 30, 0, TimeSpan.Zero));

        dto.Date.Should().Be(new DateOnly(2026, 8, 19));
        dto.TimeZone.Should().Be("Europe/Zurich");
    }

    /// <summary>
    /// The other direction, which a tenant east of UTC cannot expose: at 02:00Z the 19th it is
    /// still the 18th in Los Angeles. Asserting only the Zurich case would leave "return UTC's day"
    /// passing half the time and failing the half nobody tested.
    /// </summary>
    [Fact]
    public void Before_UTC_midnight_a_western_tenant_is_still_on_the_previous_day()
    {
        var (dto, _) = Ask("America/Los_Angeles", new DateTimeOffset(2026, 8, 19, 2, 0, 0, TimeSpan.Zero));

        dto.Date.Should().Be(new DateOnly(2026, 8, 18));
        dto.TimeZone.Should().Be("America/Los_Angeles");
    }

    /// <summary>
    /// Zurich is +01:00 in January and +02:00 in May, so the boundary moves with DST. A fixed
    /// offset would be right for half the year — the same thing #363 was about.
    /// </summary>
    [Fact]
    public void The_boundary_follows_DST_rather_than_a_fixed_offset()
    {
        // 23:30Z on 16 January is 00:30 on the 17th in Zurich (+01:00) …
        Ask("Europe/Zurich", new DateTimeOffset(2026, 1, 16, 23, 30, 0, TimeSpan.Zero))
            .Dto.Date.Should().Be(new DateOnly(2026, 1, 17));

        // … while the same 23:30Z in July is already the 17th too, but 22:30Z is ALSO the 17th
        // (+02:00), and in January 22:30Z is still the 16th. That pair is the DST difference.
        Ask("Europe/Zurich", new DateTimeOffset(2026, 7, 16, 22, 30, 0, TimeSpan.Zero))
            .Dto.Date.Should().Be(new DateOnly(2026, 7, 17));
        Ask("Europe/Zurich", new DateTimeOffset(2026, 1, 16, 22, 30, 0, TimeSpan.Zero))
            .Dto.Date.Should().Be(new DateOnly(2026, 1, 16));
    }

    /// <summary>
    /// A day cached for an hour is a wrong day for an hour, and this endpoint exists only to be
    /// current. The header is the contract — nothing else between here and the guest knows that.
    /// </summary>
    [Fact]
    public void The_answer_refuses_to_be_cached()
    {
        var (_, http) = Ask("Europe/Zurich", new DateTimeOffset(2026, 8, 18, 22, 30, 0, TimeSpan.Zero));

        http.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }
}
