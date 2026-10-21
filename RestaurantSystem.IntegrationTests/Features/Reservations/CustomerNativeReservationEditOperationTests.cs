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
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 3")]
public sealed partial class CustomerNativeReservationEditOperationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly DateTime Day = new(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc);
    private readonly Mock<ITenantModules> _modules = new();
    private readonly Mock<ITenantClock> _clock = new();
    private Guid _booking, _first, _second, _spare;
    private readonly string _session = Guid.NewGuid().ToString("D");
    private readonly Mock<IEmailService> _email = new();
    private Func<Reservation, ReservationEdit, Task>? _onMail;
    private int _mailCalls;
    private readonly EditSaveBoundary _saveBoundary = new();
    private const string Route = "/api/customer-mobile/reservations/edit";
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
        services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(_saveBoundary));
        services.RemoveAll<IEmailService>(); services.AddSingleton(_email.Object);
        services.RemoveAll<IReservationChangedMailer>();
        services.AddScoped<IReservationChangedMailer>(provider => new ObservedMailer(
            ActivatorUtilities.CreateInstance<ReservationChangedMailer>(provider), async (row, edit) =>
            {
                Interlocked.Increment(ref _mailCalls);
                if (_onMail is not null) await _onMail(row, edit);
            }));
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
    private CustomerReservationEditDto Request() => new(1, Guid.NewGuid(), Tenant, _booking, "Europe/Zurich",
        DateOnly.FromDateTime(Day), TimeSpan.FromHours(20), TimeSpan.FromHours(22), 6,
        "Changed customer", "changed@example.test", "+41000000001", "Changed request");
    private async Task<HttpResponseMessage> SendAsync(string path, object body, string? session = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Route + "/" + path);
        message.Headers.Add("X-Session-Id", session ?? _session); message.Content = JsonContent.Create(body);
        return await Client.SendAsync(message);
    }
    private async Task<(CustomerReservationEditDto Request, CustomerReservationEditPreparedDto Review)> PrepareAsync(CustomerReservationEditDto? input = null)
    {
        var request = input ?? Request();
        using var response = await SendAsync("prepare", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CustomerReservationEditPreparedDto>>(JsonOptions);
        Assert.NotNull(body); Assert.True(body.Success); Assert.NotNull(body.Data);
        return (request with { PayloadHash = body.Data.Proof.PayloadHash, OwnerProof = body.Data.Proof.OwnerProof }, body.Data);
    }
    private async Task<JsonElement> DecideAsync(string path, object input)
    {
        using var response = await SendAsync(path, input); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text); Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.NotNull(body); Assert.True(body.Success);
        Assert.Equal(new[] { "schemaVersion", "operationId", "outcome", "reservationId" }, body.Data.EnumerateObject().Select(row => row.Name));
        foreach (var forbidden in new[] { "customerEmail", "customerPhone", "tableId", "revision", "ownerProof", "private-qr", "editor-staff-note" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        return body.Data;
    }
    private async Task AssertUntouchedAsync(Guid revision)
    {
        await using var db = DatabaseFixture.CreateContext(); var row = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(revision, row.Revision); Assert.Equal(ReservationStatus.Confirmed, row.Status);
        Assert.Equal("editor@example.test", row.CustomerEmail); Assert.Equal(TimeSpan.FromHours(18), row.StartTime);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
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
    private sealed class EditSaveBoundary : SaveChangesInterceptor
    {
        public Func<ApplicationDbContext, Task>? AfterReceipt { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext db && AfterReceipt is { } callback
                && db.ChangeTracker.Entries<CustomerReservationOperation>().Any())
            { AfterReceipt = null; await callback(db); }
            return result;
        }
    }
    private sealed class ObservedMailer(IReservationChangedMailer inner, Func<Reservation, ReservationEdit, Task> observe) : IReservationChangedMailer
    {
        public async Task SendAsync(Reservation reservation, string tableNumber, ReservationEdit edit, CancellationToken cancellationToken)
        { await observe(reservation, edit); await inner.SendAsync(reservation, tableNumber, edit, cancellationToken); }
    }
}
