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
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 4")]
public sealed partial class CustomerNativeReservationAvailabilityTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/customer-mobile/reservations/availability";
    private const string Query = "?schemaVersion=1&date=2030-05-17&numberOfGuests=6";
    private readonly MutableClock _clock = new(new DateTimeOffset(2030, 5, 10, 9, 0, 0, TimeSpan.Zero), "America/Los_Angeles");
    private readonly Mock<ITenantModules> _modules = new();
    private Action? _afterSlots;
    private Guid _first, _second;
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
        services.RemoveAll<ITenantClock>();
        services.AddSingleton<ITenantClock>(_clock);
        _modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        _modules.SetupGet(value => value.IsEnforced).Returns(true);
        _modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Reservations]);
        services.RemoveAll<ITenantModules>();
        services.AddSingleton(_modules.Object);
        services.AddScoped<IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>>(provider =>
            new ObservedSlots(ActivatorUtilities.CreateInstance<GetAvailableTimeSlotsQueryHandler>(provider), () => _afterSlots?.Invoke()));
    }

    [Fact]
    public async Task Anonymous_availability_returns_exact_split_shift_times_and_no_table_credentials_or_layout()
    {
        await SeedAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.GetAsync("/api/customer-mobile/reservations?schemaVersion=1")).StatusCode);
        // Positive control: this fixture really populates fields the existing web contract returns.
        var legacy = await Client.GetStringAsync("/api/reservations/available-slots?date=2030-05-17&numberOfGuests=6");
        Assert.Contains("private-qr", legacy, StringComparison.Ordinal);
        var page = await ReadAsync();
        Assert.Equal(new[] { "schemaVersion", "date", "timeZone", "numberOfGuests", "slots" }, page.EnumerateObject().Select(value => value.Name));
        Assert.Equal("2030-05-17", page.GetProperty("date").GetString());
        Assert.Equal("America/Los_Angeles", page.GetProperty("timeZone").GetString());
        Assert.Equal(6, page.GetProperty("numberOfGuests").GetInt32());
        Assert.Equal(new[] { "11:00:00", "11:30:00", "12:00:00", "12:30:00", "13:00:00",
            "18:00:00", "18:30:00", "19:00:00", "19:30:00", "20:00:00", "20:30:00", "21:00:00" }, Starts(page));
        Assert.All(page.GetProperty("slots").EnumerateArray(), slot =>
            Assert.Equal(new[] { "startTime", "endTime" }, slot.EnumerateObject().Select(value => value.Name)));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Empty(await context.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Combined_occupancy_and_party_capacity_are_applied_without_returning_table_ids()
    {
        await SeedAsync();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Reservations.Add(new Reservation
            {
                TableId = _first,
                CustomerName = "private-guest",
                CustomerEmail = "private@example.test",
                CustomerPhone = "private",
                ReservationDate = new DateTime(2030, 5, 17, 9, 15, 0, DateTimeKind.Utc),
                StartTime = new TimeSpan(18, 0, 0),
                EndTime = new TimeSpan(20, 0, 0),
                NumberOfGuests = 6,
                Status = ReservationStatus.Confirmed,
                CreatedBy = "test",
                CombinedTables = [new ReservationTable { TableId = _second, CreatedBy = "test" }]
            });
            await context.SaveChangesAsync();
        }
        Assert.Equal(new[] { "11:00:00", "11:30:00", "12:00:00", "12:30:00", "13:00:00", "20:00:00", "20:30:00", "21:00:00" }, Starts(await ReadAsync()));
        Assert.Empty(Starts(await ReadAsync("?schemaVersion=1&date=2030-05-17&numberOfGuests=7")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=2&date=2030-05-17&numberOfGuests=6")]
    [InlineData("?schemaVersion=1&numberOfGuests=6")]
    [InlineData("?schemaVersion=1&date=bad&numberOfGuests=6")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=0")]
    [InlineData("?schemaVersion=1&date=2030-05-17&numberOfGuests=21")]
    [InlineData("?schemaVersion=1&date=2030-05-17")]
    [InlineData("?schemaVersion=1&date=2030-05-09&numberOfGuests=6")]
    public async Task Invalid_schema_date_or_party_is_rejected_with_an_enabled_positive_control(string query)
    {
        await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + query)).StatusCode);
        Assert.NotEmpty(Starts(await ReadAsync()));
    }

    private async Task<JsonElement> ReadAsync(string query = Query)
    {
        using var response = await Client.GetAsync(Route + query);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(response.Headers.CacheControl?.NoStore);
        foreach (var forbidden in new[] { "private", "tableId", "tableNumber", "availableTables", "notes", "qrCode", "positionX", "customerEmail" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        return envelope.Data;
    }
    private static string?[] Starts(JsonElement page) => page.GetProperty("slots").EnumerateArray()
        .Select(slot => slot.GetProperty("startTime").GetString()).ToArray();
    private async Task SeedAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        context.WorkingHours.RemoveRange(await context.WorkingHours.ToListAsync());
        var day = new WorkingHours { DayOfWeek = DayOfWeek.Friday, IsActive = true, CreatedBy = "test" };
        day.Shifts.Add(new WorkingHoursShift { OpenTime = new TimeSpan(11, 0, 0), CloseTime = new TimeSpan(15, 0, 0), CreatedBy = "test" });
        day.Shifts.Add(new WorkingHoursShift { OpenTime = new TimeSpan(18, 0, 0), CloseTime = new TimeSpan(23, 0, 0), CreatedBy = "test" });
        context.WorkingHours.Add(day);
        var tables = new[] { Table("NATIVE-1", 4), Table("NATIVE-2", 2), Table("INACTIVE", 20) };
        tables[2].IsActive = false;
        context.Tables.AddRange(tables);
        await context.SaveChangesAsync();
        _first = tables[0].Id; _second = tables[1].Id;
    }
    private static Table Table(string number, int capacity) => new()
    {
        TableNumber = number,
        MaxGuests = capacity,
        IsActive = true,
        QRCodeData = "private-qr",
        Notes = "private-staff",
        PositionX = 42,
        CreatedBy = "test"
    };
    public sealed class ObservedSlots(GetAvailableTimeSlotsQueryHandler inner, Action afterRead)
        : IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>
    {
        public async Task<ApiResponse<AvailableTimeSlotsDto>> Handle(GetAvailableTimeSlotsQuery query, CancellationToken cancellationToken)
        {
            var result = await inner.Handle(query, cancellationToken);
            afterRead();
            return result;
        }
    }
}
