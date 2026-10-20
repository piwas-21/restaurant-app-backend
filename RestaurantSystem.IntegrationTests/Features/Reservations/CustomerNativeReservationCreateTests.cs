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
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 4")]
public sealed partial class CustomerNativeReservationCreateTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/customer-mobile/reservations/create";
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly DateTime Day = new(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _session = Guid.NewGuid().ToString("D");
    private readonly Mock<ITenantModules> _modules = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ITenantClock> _clock = new();
    private Action? _afterSlots;
    private Func<Reservation, Task>? _onMail;
    private int _mailCalls;
    private readonly Dictionary<string, CustomerReservationProofDto> _proofs = new();
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
        services.RemoveAll<IReservationCreatedMailer>();
        services.AddScoped<IReservationCreatedMailer>(provider => new ObservedMailer(
            ActivatorUtilities.CreateInstance<ReservationCreatedMailer>(provider), async row =>
            {
                _mailCalls++;
                if (_onMail is not null) await _onMail(row);
            }));
        services.AddScoped<IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>>(provider =>
            new CustomerNativeReservationAvailabilityTests.ObservedSlots(
                ActivatorUtilities.CreateInstance<GetAvailableTimeSlotsQueryHandler>(provider), () => _afterSlots?.Invoke()));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var db = DatabaseFixture.CreateContext();
        db.WorkingHours.RemoveRange(await db.WorkingHours.ToListAsync());
        var hours = new WorkingHours { DayOfWeek = DayOfWeek.Friday, IsActive = true, CreatedBy = "test" };
        hours.Shifts.Add(new WorkingHoursShift { OpenTime = TimeSpan.FromHours(18), CloseTime = TimeSpan.FromHours(23), CreatedBy = "test" });
        db.WorkingHours.Add(hours);
        var first = new Table { TableNumber = "CREATE-A", MaxGuests = 4, QRCodeData = "private-qr", Notes = "private-staff", CreatedBy = "test" };
        var second = new Table { TableNumber = "CREATE-B", MaxGuests = 2, CreatedBy = "test" };
        db.Tables.AddRange(first, second);
        await db.SaveChangesAsync(); _first = first.Id; _second = second.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_create_replay_and_cancel_attempt_have_one_booking_one_decision_and_one_mail(bool signedIn)
    {
        if (signedIn) Authenticate();
        var request = Request();
        var first = await ResolveAsync(request);
        var id = first.GetProperty("reservationId").GetGuid();
        Assert.Equal("Applied", first.GetProperty("outcome").GetString());
        Assert.Equal(first.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Equal(first.GetRawText(), (await ResolveAsync(request, true)).GetRawText());
        await using var db = DatabaseFixture.CreateContext();
        var row = await db.Reservations.Include(value => value.CombinedTables).SingleAsync();
        Assert.Equal(id, row.Id); Assert.Equal(signedIn ? Owner : (Guid?)null, row.CustomerId);
        Assert.Equal(Day, row.ReservationDate); Assert.Equal(ReservationStatus.Pending, row.Status);
        Assert.Equal(6, row.NumberOfGuests);
        Assert.Equal(new[] { _first, _second }.Order(), row.CombinedTables.Select(value => value.TableId).Append(row.TableId).Order());
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(1, _mailCalls);
    }

    [Fact]
    public async Task An_unseen_cancel_attempt_seals_late_execution_after_native_access_is_withdrawn()
    {
        var request = Request();
        await PrepareAsync(request);
        Mobile.Enabled = false;
        var cancelled = await ResolveAsync(request, true);
        Assert.Equal("Cancelled", cancelled.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("reservationId").ValueKind);
        Mobile.Enabled = true;
        Assert.Equal(cancelled.GetRawText(), (await ResolveAsync(request)).GetRawText());
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Equal(0, _mailCalls);
    }

    [Fact]
    public async Task Applied_decisions_survive_booking_deletion_and_full_native_withdrawal_without_replaying_mail()
    {
        var request = Request(); var accepted = await ResolveAsync(request);
        await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteDeleteAsync();
        Mobile.Enabled = false;
        Assert.Equal(accepted.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await ResolveAsync(request, true)).GetRawText());
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Equal(1, _mailCalls);
    }

    [Fact]
    public async Task Changed_payload_conflicts_and_another_session_cannot_probe_or_seal_an_existing_decision()
    {
        var request = Request(); var accepted = await ResolveAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(request with { CustomerName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(request, true, Guid.NewGuid().ToString("D"))).StatusCode);
        Assert.Equal(accepted.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Equal(1, _mailCalls);
    }

    private static CustomerReservationCreateDto Request() => new(1, Guid.NewGuid(), Tenant, "Europe/Zurich",
        DateOnly.FromDateTime(Day), TimeSpan.FromHours(18), TimeSpan.FromHours(20), 6,
        "Native guest", "guest@example.test", null, "Customer request");
    private CustomerMobileSettings Mobile => Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
    private void Authenticate(UserRole role = UserRole.Customer, bool expired = false) =>
        Client.DefaultRequestHeaders.Authorization = new("Bearer", CustomerBasketTokens.Create(Factory.Services, role, expired));
    private static string ProofKey(CustomerReservationCreateDto request) => JsonSerializer.Serialize(request with { PayloadHash = null, OwnerProof = null });
    private async Task<CustomerReservationProofDto> PrepareAsync(CustomerReservationCreateDto request)
    {
        var proof = await ProofAsync(request);
        Assert.True(_proofs.ContainsKey(ProofKey(request)), "A valid preparation must come from the real server.");
        return proof;
    }
    private async Task<CustomerReservationProofDto> ProofAsync(CustomerReservationCreateDto request)
    {
        if (request.PayloadHash is { } explicitHash)
            return new(request.SchemaVersion, request.OperationId, request.TenantId, explicitHash, request.OwnerProof ?? string.Empty);
        var key = ProofKey(request);
        if (_proofs.TryGetValue(key, out var existing)) return existing;
        using var prepare = new HttpRequestMessage(HttpMethod.Post, Route + "/prepare");
        prepare.Headers.Add("X-Session-Id", _session);
        prepare.Content = JsonContent.Create(request with { PayloadHash = null, OwnerProof = null });
        using var prepared = await Client.SendAsync(prepare);
        if (prepared.IsSuccessStatusCode)
        {
            var envelope = (await prepared.Content.ReadFromJsonAsync<ApiResponse<CustomerReservationProofDto>>())!;
            Assert.True(envelope.Success); Assert.NotNull(envelope.Data);
            _proofs.Add(key, envelope.Data);
            return envelope.Data;
        }
        // Negative input/auth tests MUST still exercise create/cancel themselves, even if preparation refuses.
        return new(request.SchemaVersion, request.OperationId, request.TenantId, new string('a', 64), "invalid");
    }
    private async Task<HttpResponseMessage> SendAsync(CustomerReservationCreateDto request, bool cancel = false, string? session = null)
    {
        var proof = await ProofAsync(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, Route + (cancel ? "/cancel-attempt" : ""));
        message.Headers.Add("X-Session-Id", session ?? _session);
        message.Content = cancel ? JsonContent.Create(proof) : JsonContent.Create(request with { PayloadHash = proof.PayloadHash, OwnerProof = proof.OwnerProof });
        return await Client.SendAsync(message);
    }
    private async Task<JsonElement> ResolveAsync(CustomerReservationCreateDto request, bool cancel = false)
    {
        using var response = await SendAsync(request, cancel); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text); Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!; Assert.True(result.Success);
        Assert.Equal(new[] { "schemaVersion", "operationId", "outcome", "reservationId" }, result.Data.EnumerateObject().Select(value => value.Name));
        Assert.Equal(request.OperationId, result.Data.GetProperty("operationId").GetGuid());
        return result.Data;
    }
    private sealed class ObservedMailer(IReservationCreatedMailer inner, Func<Reservation, Task> observe) : IReservationCreatedMailer
    {
        public async Task SendAsync(Reservation row, string table, CancellationToken cancellationToken)
        {
            await observe(row); await inner.SendAsync(row, table, cancellationToken);
        }
    }
}
