using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 4")]
public sealed partial class CustomerNativeReservationReadTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/customer-mobile/reservations";
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid Foreign = Guid.Parse(TestAuthHandler.AdminUserId);
    private static readonly Guid TableId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTime Day = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Mock<ITenantModules> _modules = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
            settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["reservations"];
        });
        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        services.RemoveAll<ITenantClock>();
        services.AddSingleton(clock.Object);
        _modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        _modules.SetupGet(value => value.IsEnforced).Returns(true);
        _modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Reservations]);
        services.RemoveAll<ITenantModules>();
        services.AddSingleton(_modules.Object);
    }

    [Fact]
    public async Task History_preserves_calendar_day_and_stable_cursor_without_exposing_staff_or_contact_data()
    {
        Authenticate();
        await SeedAsync(Enumerable.Range(1, 23).Reverse().Select(index =>
        {
            var row = Reservation(index, Owner);
            row.ReservationDate = Day.AddHours(9).AddMinutes(15); // Existing writes need not normalize midnight.
            return row;
        })
            .Concat([Reservation(99, Foreign), Reservation(100, null)]));
        await using (var context = DatabaseFixture.CreateContext())
        {
            var deleted = Reservation(0, Owner);
            context.Reservations.Add(deleted);
            await context.SaveChangesAsync();
            context.Reservations.Remove(deleted);
            await context.SaveChangesAsync();
        }
        var page = await ReadAsync("?schemaVersion=1&customerId=" + Foreign);
        Assert.Equal("America/Los_Angeles", page.GetProperty("timeZone").GetString());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(1, 20).Select(Id), items.Select(item => item.GetProperty("reservationId").GetGuid()));
        Assert.Equal("2026-11-01", items[0].GetProperty("reservationDate").GetString());
        Assert.Equal("18:30:00", items[0].GetProperty("startTime").GetString());
        Assert.Equal(new[] { "reservationId", "reservationDate", "startTime", "endTime", "numberOfGuests", "status", "specialRequests" },
            items[0].EnumerateObject().Select(property => property.Name));
        var cursor = page.GetProperty("next");
        Assert.Equal(Day.AddHours(9).AddMinutes(15), cursor.GetProperty("reservationDate").GetDateTime());
        var second = await ReadAsync(Cursor(cursor));
        Assert.Equal(Enumerable.Range(21, 3).Select(Id), second.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("reservationId").GetGuid()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);
        var detail = await ReadAsync("/" + Id(1) + "?schemaVersion=1");
        Assert.Equal("2026-11-01", detail.GetProperty("reservationDate").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + "/" + Id(99) + "?schemaVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + "/" + Id(101) + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    public async Task Both_reads_require_the_authenticated_customer(UserRole? role)
    {
        if (role is not null) Authenticate(role.Value);
        foreach (var path in new[] { "", "/" + Id(1) })
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                (await Client.GetAsync(Route + path + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=0")]
    [InlineData("?schemaVersion=2")]
    [InlineData("?schemaVersion=1&beforeDate=2026-11-01")]
    [InlineData("?schemaVersion=1&beforeId=11111111-1111-4111-8111-111111111111")]
    [InlineData("?schemaVersion=1&beforeDate=2026-11-01T00:00:00Z&beforeStartTime=bad&beforeId=11111111-1111-4111-8111-111111111111")]
    [InlineData("?schemaVersion=1&beforeDate=bad&beforeStartTime=18:30:00&beforeId=11111111-1111-4111-8111-111111111111")]
    [InlineData("?schemaVersion=1&beforeDate=2026-11-01T00:00:00Z&beforeStartTime=18:30:00&beforeId=00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_version_and_cursor_fail_with_a_working_read_control(string query)
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=0")]
    [InlineData("?schemaVersion=2")]
    public async Task Detail_requires_the_supported_version_before_returning_an_owned_row(string query)
    {
        Authenticate();
        await SeedAsync([Reservation(1, Owner)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + "/" + Id(1) + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + "/" + Id(1) + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("disabled")]
    [InlineData("capability")]
    [InlineData("module")]
    [InlineData("currency")]
    [InlineData("account")]
    [InlineData("role")]
    public async Task Fresh_capability_and_database_owner_are_required(string withdrawal)
    {
        Authenticate();
        await SeedAsync([Reservation(1, Owner)]);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + "/" + Id(1) + "?schemaVersion=1")).StatusCode);
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        if (withdrawal == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Reservations)).Returns(false);
        else if (withdrawal == "currency")
        {
            await using var context = DatabaseFixture.CreateContext();
            await context.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
        }
        else if (withdrawal == "capability") mobile.EnabledCapabilities = [];
        else if (withdrawal is "disabled" or "read-only") mobile.Lifecycle = withdrawal;
        else
        {
            await using var context = DatabaseFixture.CreateContext();
            var user = await context.Users.SingleAsync(value => value.Id == Owner);
            if (withdrawal == "account") user.IsDeleted = true; else user.Role = UserRole.Admin;
            await context.SaveChangesAsync();
        }
        foreach (var path in new[] { "", "/" + Id(1) })
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + path + "?schemaVersion=1")).StatusCode);
    }

    private void Authenticate(UserRole role = UserRole.Customer) => Client.DefaultRequestHeaders.Authorization =
        new("Bearer", CustomerBasketTokens.Create(Factory.Services, role, userId: Owner.ToString()));
    private async Task<JsonElement> ReadAsync(string suffix)
    {
        using var response = await Client.GetAsync(Route + suffix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "native-private", "customerEmail", "customerPhone", "customerId", "tableId", "notes", "quickActionToken" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
    }
    private async Task SeedAsync(IEnumerable<Reservation> reservations)
    {
        await using var context = DatabaseFixture.CreateContext();
        context.Tables.Add(new Table { Id = TableId, TableNumber = "P4-TEST", MaxGuests = 4, CreatedBy = "test" });
        context.Reservations.AddRange(reservations);
        await context.SaveChangesAsync();
    }
    private static Reservation Reservation(int index, Guid? owner) => new()
    {
        Id = Id(index),
        CustomerId = owner,
        TableId = TableId,
        ReservationDate = Day,
        StartTime = new TimeSpan(18, 30, 0),
        EndTime = new TimeSpan(20, 0, 0),
        NumberOfGuests = 2,
        CustomerName = "native-private",
        CustomerEmail = "native-private@example.test",
        CustomerPhone = "native-private",
        Notes = "native-private",
        SpecialRequests = "Window seat",
        CreatedBy = "test"
    };
    private static Guid Id(int index) => Guid.Parse(index.ToString("x8", CultureInfo.InvariantCulture) + "-0000-4000-8000-000000000000");
    private static string Cursor(JsonElement cursor) => "?schemaVersion=1&beforeDate=" + Uri.EscapeDataString(cursor.GetProperty("reservationDate").GetString()!)
        + "&beforeStartTime=" + Uri.EscapeDataString(cursor.GetProperty("startTime").GetString()!)
        + "&beforeId=" + cursor.GetProperty("reservationId").GetGuid();
}
