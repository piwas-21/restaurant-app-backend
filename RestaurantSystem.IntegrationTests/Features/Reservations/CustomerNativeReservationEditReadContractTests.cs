using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditReadTests
{
    [Fact]
    public async Task Context_returns_only_owned_editor_fields_preserves_calendar_day_and_never_writes()
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        var before = await db.Reservations.AsNoTracking().SingleAsync();
        var value = await ReadAsync();
        Assert.Equal(new[] { "schemaVersion", "tenantId", "timeZone", "reservation", "contact" }, value.EnumerateObject().Select(row => row.Name));
        Assert.Equal(Tenant, value.GetProperty("tenantId").GetGuid());
        Assert.Equal("Europe/Zurich", value.GetProperty("timeZone").GetString());
        Assert.Equal("2030-05-17", value.GetProperty("reservation").GetProperty("reservationDate").GetString());
        Assert.Equal(new[] { "customerName", "customerEmail", "customerPhone" }, value.GetProperty("contact").EnumerateObject().Select(row => row.Name));
        Assert.Equal("Editor customer", value.GetProperty("contact").GetProperty("customerName").GetString());
        Assert.Equal("editor@example.test", value.GetProperty("contact").GetProperty("customerEmail").GetString());
        Assert.Equal("+41000000000", value.GetProperty("contact").GetProperty("customerPhone").GetString());
        var ordinary = await Client.GetStringAsync($"/api/customer-mobile/reservations/{_booking:D}?schemaVersion=1");
        Assert.DoesNotContain("customerEmail", ordinary, StringComparison.Ordinal);
        var after = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Status, after.Status);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }
    [Fact]
    public async Task Owned_availability_ignores_only_its_occupancy_and_never_reassigns_to_a_larger_spare_table()
    {
        Authenticate(); var value = await ReadAsync(AvailabilityQuery);
        Assert.Equal(new[] { "schemaVersion", "reservationId", "date", "timeZone", "numberOfGuests", "slots" }, value.EnumerateObject().Select(row => row.Name));
        Assert.Equal(_booking, value.GetProperty("reservationId").GetGuid());
        Assert.Equal("2030-05-17", value.GetProperty("date").GetString());
        Assert.Equal(new[] { "18:00:00", "18:30:00", "19:00:00", "19:30:00", "20:00:00", "20:30:00", "21:00:00" }, Starts(value));
        Assert.Empty(Starts(await ReadAsync(AvailabilityQuery.Replace("numberOfGuests=6", "numberOfGuests=7", StringComparison.Ordinal))));
        await using var db = DatabaseFixture.CreateContext();
        Assert.Equal(8, await db.Tables.Where(row => row.Id == _spare).Select(row => row.MaxGuests).SingleAsync());
        Assert.Equal(_first, (await db.Reservations.SingleAsync()).TableId);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public async Task Another_booking_on_any_assigned_child_blocks_the_whole_arrangement_even_when_the_primary_fits(int guests)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        var other = Booking(_second, null); other.NumberOfGuests = 1;
        db.Reservations.Add(other); await db.SaveChangesAsync();
        Assert.Equal(new[] { "20:00:00", "20:30:00", "21:00:00" }, Starts(await ReadAsync(
            AvailabilityQuery.Replace("numberOfGuests=6", "numberOfGuests=" + guests, StringComparison.Ordinal))));
    }
    [Fact]
    public async Task Public_availability_cannot_bind_the_internal_own_booking_exclusion()
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        await db.Tables.Where(row => row.Id == _spare).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false));
        const string path = "/api/customer-mobile/reservations/availability?schemaVersion=1&date=2030-05-17&numberOfGuests=6";
        foreach (var query in new[] { path, path + "&excludedReservationId=" + _booking })
        {
            var page = await Client.GetFromJsonAsync<ApiResponse<JsonElement>>(query);
            Assert.NotNull(page); Assert.True(page.Success);
            Assert.Equal(new[] { "20:00:00", "20:30:00", "21:00:00" }, Starts(page.Data));
        }
        Assert.Equal("18:00:00", Starts(await ReadAsync(AvailabilityQuery))[0]);
    }
    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public async Task Both_editor_reads_require_the_authenticated_customer(UserRole? role)
    {
        if (role is not null) Authenticate(role.Value);
        foreach (var suffix in new[] { "?schemaVersion=1", AvailabilityQuery })
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                (await Client.GetAsync(Route + suffix)).StatusCode);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("guest")]
    public async Task Foreign_guest_and_missing_bookings_share_the_same_unavailable_response(string mode)
    {
        Authenticate(); var own = _booking;
        if (mode == "missing") _booking = Guid.NewGuid();
        else
        {
            await using var db = DatabaseFixture.CreateContext();
            var row = Booking(_first, mode == "guest" ? null : Guid.Parse(Common.TestAuthHandler.AdminUserId));
            db.Reservations.Add(row); await db.SaveChangesAsync(); _booking = row.Id;
        }
        foreach (var suffix in new[] { "?schemaVersion=1", AvailabilityQuery })
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + suffix)).StatusCode);
        _booking = own; Assert.Equal(own, (await ReadAsync()).GetProperty("reservation").GetProperty("reservationId").GetGuid());
    }
    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=0")]
    [InlineData("?schemaVersion=2")]
    public async Task Editor_context_requires_the_supported_schema(string query)
    {
        Authenticate(); Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + query)).StatusCode);
        Assert.Equal(_booking, (await ReadAsync()).GetProperty("reservation").GetProperty("reservationId").GetGuid());
    }
    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=2&date=2030-05-17&numberOfGuests=6&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&numberOfGuests=6&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&date=bad&numberOfGuests=6&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&date=2030-05-17&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=0&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=21&timeZone=Europe%2FZurich")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=6")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=6&timeZone=UTC")]
    [InlineData("?schemaVersion=1&date=2030-05-09&numberOfGuests=6&timeZone=Europe%2FZurich")]
    public async Task Owned_availability_rejects_incomplete_or_rebound_calendar_inputs(string query)
    {
        Authenticate(); Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + "/availability" + query)).StatusCode);
        Assert.NotEmpty(Starts(await ReadAsync(AvailabilityQuery)));
    }
}
