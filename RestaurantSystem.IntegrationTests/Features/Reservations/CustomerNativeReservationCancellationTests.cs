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
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 4")]
public sealed partial class CustomerNativeReservationCancellationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/customer-mobile/reservations/cancellation";
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly DateTime Day = new(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _session = Guid.NewGuid().ToString("D");
    private readonly Mock<ITenantModules> _modules = new();
    private readonly Mock<ITenantClock> _clock = new();
    private readonly Mock<IEmailService> _email = new();
    private Func<Reservation, Task>? _onMail;
    private Func<ApplicationDbContext, Task>? _beforeBooking;
    private Action? _afterAccepting;
    private int _mailCalls;
    private Guid _booking, _table;

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
        services.RemoveAll<IEmailService>(); services.AddSingleton(_email.Object);
        services.RemoveAll<IReservationRejectedMailer>();
        services.AddScoped<IReservationRejectedMailer>(provider => new ObservedMailer(
            ActivatorUtilities.CreateInstance<ReservationRejectedMailer>(provider), async row =>
        {
            Interlocked.Increment(ref _mailCalls);
            if (_onMail is not null) await _onMail(row);
        }));
        services.RemoveAll<ICustomerReservationCancellationAccess>();
        services.AddScoped<ICustomerReservationCancellationAccess>(provider => new ObservedAccess(
            ActivatorUtilities.CreateInstance<CustomerReservationCancellationAccess>(provider),
            provider.GetRequiredService<ApplicationDbContext>(), async db =>
            { if (_beforeBooking is not null) await _beforeBooking(db); }, () => _afterAccepting?.Invoke()));
    }
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var db = DatabaseFixture.CreateContext();
        var table = new Table { TableNumber = "CANCEL-A", MaxGuests = 4, QRCodeData = "private-qr", Notes = "private-staff", CreatedBy = "test" };
        db.Tables.Add(table); await db.SaveChangesAsync(); _table = table.Id;
        var row = new Reservation
        {
            CustomerId = Owner,
            TableId = _table,
            CustomerName = "Private customer",
            CustomerEmail = "private@example.test",
            CustomerPhone = "+41000000000",
            Notes = "operator-private",
            SpecialRequests = "Customer request",
            ReservationDate = Day,
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            Status = ReservationStatus.Confirmed,
            CreatedBy = "test"
        };
        db.Reservations.Add(row); await db.SaveChangesAsync(); _booking = row.Id;
    }
    private CustomerMobileSettings Mobile => Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
    private void Authenticate(UserRole role = UserRole.Customer, bool expired = false) =>
        Client.DefaultRequestHeaders.Authorization = new("Bearer", CustomerBasketTokens.Create(Factory.Services, role, expired));
    private CustomerReservationCancellationDto Request() => new(1, Guid.NewGuid(), Tenant, _booking, "Europe/Zurich");
    private async Task<HttpResponseMessage> SendAsync(string path, object body, string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/" + path);
        request.Headers.Add("X-Session-Id", session ?? _session); request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    private async Task<CustomerReservationCancellationPreparedDto> PrepareAsync(CustomerReservationCancellationDto? request = null)
    {
        using var response = await SendAsync("prepare", request ?? Request());
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CustomerReservationCancellationPreparedDto>>(JsonOptions);
        Assert.NotNull(body); Assert.True(body.Success); Assert.NotNull(body.Data); return body.Data;
    }
    private async Task<JsonElement> DecideAsync(string path, CustomerReservationProofDto proof)
    {
        using var response = await SendAsync(path, proof);
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        Assert.NotNull(body); Assert.True(body.Success);
        Assert.Equal(new[] { "schemaVersion", "operationId", "outcome", "reservationId" }, body.Data.EnumerateObject().Select(value => value.Name));
        Assert.Equal(proof.OperationId, body.Data.GetProperty("operationId").GetGuid()); return body.Data;
    }
    private async Task AssertUntouchedAsync()
    {
        await using var db = DatabaseFixture.CreateContext();
        Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.SingleAsync()).Status);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    private sealed class ObservedMailer(IReservationRejectedMailer inner, Func<Reservation, Task> observe) : IReservationRejectedMailer
    {
        public async Task SendAsync(Reservation row, CancellationToken cancellationToken)
        { await observe(row); await inner.SendAsync(row, cancellationToken); }
    }
    private sealed class ObservedAccess(ICustomerReservationCancellationAccess inner, ApplicationDbContext context,
        Func<ApplicationDbContext, Task> beforeBooking, Action afterAccepting) : ICustomerReservationCancellationAccess
    {
        public string AuditIdentifier => inner.AuditIdentifier;
        public CustomerReservationOperationOwner Capture(string session, int? version, Guid operation, Guid tenant) =>
            inner.Capture(session, version, operation, tenant);
        public Task LockCustomerAsync(CustomerReservationOperationOwner owner, CancellationToken token) => inner.LockCustomerAsync(owner, token);
        public async Task<Reservation> LockBookingAsync(Guid reservation, CustomerReservationOperationOwner owner, CancellationToken token)
        { await beforeBooking(context); return await inner.LockBookingAsync(reservation, owner, token); }
        public async Task RequireAcceptingAsync(Guid tenant, string zone, CancellationToken token)
        { await inner.RequireAcceptingAsync(tenant, zone, token); afterAccepting(); }
        public void EnsureEditable(Reservation reservation) => inner.EnsureEditable(reservation);
    }
}
