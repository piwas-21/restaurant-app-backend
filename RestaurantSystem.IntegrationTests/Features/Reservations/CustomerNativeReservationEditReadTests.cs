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
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 2")]
public sealed partial class CustomerNativeReservationEditReadTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly DateTime Day = new(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc);
    private const string AvailabilityQuery = "/availability?schemaVersion=1&date=2030-05-17&numberOfGuests=6&timeZone=Europe%2FZurich";
    private readonly Mock<ITenantModules> _modules = new();
    private readonly Mock<ITenantClock> _clock = new();
    private Guid _booking, _first, _second, _spare;
    private Func<Task>? _afterSlots;
    private string Route => $"/api/customer-mobile/reservations/{_booking:D}/edit";
    private CustomerMobileSettings Mobile => Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
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
            settings.Enabled = true; settings.TenantId = Tenant; settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["reservations", "accounts"];
        });
        _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"));
        _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, 10, 11, 0, 0, TimeSpan.FromHours(2)));
        services.RemoveAll<ITenantClock>(); services.AddSingleton(_clock.Object);
        _modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        _modules.SetupGet(value => value.IsEnforced).Returns(true);
        _modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Reservations]);
        services.RemoveAll<ITenantModules>(); services.AddSingleton(_modules.Object);
        services.AddScoped<IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>>(provider =>
            new ObservedSlots(ActivatorUtilities.CreateInstance<GetAvailableTimeSlotsQueryHandler>(provider), async () =>
            { if (_afterSlots is not null) await _afterSlots(); }));
    }
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var db = DatabaseFixture.CreateContext();
        db.WorkingHours.RemoveRange(await db.WorkingHours.ToListAsync());
        var hours = new WorkingHours { DayOfWeek = DayOfWeek.Friday, IsActive = true, CreatedBy = "test" };
        hours.Shifts.Add(new WorkingHoursShift { OpenTime = TimeSpan.FromHours(18), CloseTime = TimeSpan.FromHours(23), CreatedBy = "test" });
        db.WorkingHours.Add(hours);
        var tables = new[] { Table("EDIT-A", 4), Table("EDIT-B", 2), Table("EDIT-SPARE", 8) };
        db.Tables.AddRange(tables); await db.SaveChangesAsync(); _first = tables[0].Id; _second = tables[1].Id; _spare = tables[2].Id;
        var booking = Booking(_first, Owner); booking.CombinedTables = [new ReservationTable { TableId = _second, CreatedBy = "test" }];
        db.Reservations.Add(booking); await db.SaveChangesAsync(); _booking = booking.Id;
    }
    private void Authenticate(UserRole role = UserRole.Customer, bool expired = false) =>
        Client.DefaultRequestHeaders.Authorization = new("Bearer", CustomerBasketTokens.Create(Factory.Services, role, expired));
    private async Task<JsonElement> ReadAsync(string suffix = "?schemaVersion=1")
    {
        using var response = await Client.GetAsync(Route + suffix);
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(response.Headers.CacheControl?.NoStore);
        foreach (var forbidden in new[] { "editor-staff-note", "private-qr", "tableId", "tableNumber", "availableTables", "revision", "ownerProof", "customerId", "createdBy" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.NotNull(body); Assert.True(body.Success); return body.Data;
    }
    private static string?[] Starts(JsonElement value) => value.GetProperty("slots").EnumerateArray()
        .Select(slot => slot.GetProperty("startTime").GetString()).ToArray();
    private static Table Table(string number, int capacity) => new()
    { TableNumber = number, MaxGuests = capacity, QRCodeData = "private-qr", Notes = "editor-staff-note", CreatedBy = "test" };
    private static Reservation Booking(Guid table, Guid? owner) => new()
    {
        TableId = table,
        CustomerId = owner,
        CustomerName = "Editor customer",
        CustomerEmail = "editor@example.test",
        CustomerPhone = "+41000000000",
        SpecialRequests = "Customer request",
        Notes = "editor-staff-note",
        ReservationDate = Day.AddHours(9).AddMinutes(15),
        StartTime = TimeSpan.FromHours(18),
        EndTime = TimeSpan.FromHours(20),
        NumberOfGuests = 6,
        Status = ReservationStatus.Confirmed,
        CreatedBy = "test"
    };
    public sealed class ObservedSlots(GetAvailableTimeSlotsQueryHandler inner, Func<Task> after)
        : IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>
    {
        public async Task<ApiResponse<AvailableTimeSlotsDto>> Handle(GetAvailableTimeSlotsQuery query, CancellationToken cancellationToken)
        { var result = await inner.Handle(query, cancellationToken); await after(); return result; }
    }
}
