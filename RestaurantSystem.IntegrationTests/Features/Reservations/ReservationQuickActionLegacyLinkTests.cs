using System.Net;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.Reservations.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>Old unsigned links never prove a current revision, regardless of historical grace settings.</summary>
[Collection("Database Lane 4")]
public class ReservationQuickActionLegacyLinkTests : IntegrationTestBase
{
    private static readonly Guid TableId = Guid.Parse("eeeeeeee-0402-0000-0000-000000000002");

    private const string RefusalMarker = "This link can no longer be used";

    public ReservationQuickActionLegacyLinkTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Tables.Add(new Table { Id = TableId, TableNumber = "T-402L", MaxGuests = 4, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_unsigned_link_is_refused_even_inside_the_former_window()
    {
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-1));
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/reservations/{id}/quick-approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task A_token_less_link_is_refused_once_its_own_booking_outruns_the_window()
    {
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-30));
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/reservations/{id}/quick-approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task A_token_less_link_cannot_be_replayed_after_the_booking_was_decided()
    {
        // Neither unsigned action grants authority, even for a young Pending booking.
        var id = await SeedReservationAsync(createdAt: DateTime.UtcNow.AddDays(-1));
        AuthenticateAsAnonymous();

        (await Client.GetAsync($"/api/reservations/{id}/quick-approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await Client.GetAsync($"/api/reservations/{id}/quick-reject");

        (await replay.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending, "neither unsigned action can decide the booking");
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_genuinely_signed_unexpired_version1_link_is_refused_with_a_current_version2_positive_control(string action)
    {
        var id = await SeedReservationAsync(DateTime.UtcNow.AddDays(-1));
        var options = Factory.Services.GetRequiredService<IOptions<ReservationQuickActionSettings>>().Value;
        var jwt = Factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var material = string.IsNullOrWhiteSpace(options.SigningKey) ? jwt.Secret : options.SigningKey;
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(material), 32, salt: null,
            info: Encoding.UTF8.GetBytes("rumi:reservation-quick-action:v1"));
        var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var payload = string.Create(CultureInfo.InvariantCulture, $"v1|{id:N}|{action}|{(int)ReservationStatus.Pending}|{expires}");
        var old = string.Create(CultureInfo.InvariantCulture, $"{expires}.{Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}");
        AuthenticateAsAnonymous();
        var path = $"/api/reservations/{id}/quick-{action}?token=";
        var oldPage = await Client.GetStringAsync(path + Uri.EscapeDataString(old));
        Assert.Contains(RefusalMarker, oldPage, StringComparison.Ordinal); Assert.Equal(ReservationStatus.Pending, await StatusOfAsync(id));
        await using var db = DatabaseFixture.CreateContext();
        var revision = await db.Reservations.Where(row => row.Id == id).Select(row => row.Revision).SingleAsync();
        var intent = action == "approve" ? ReservationQuickAction.Approve : ReservationQuickAction.Reject;
        var fresh = Factory.Services.GetRequiredService<IReservationQuickActionLinks>().Mint(id, intent, ReservationStatus.Pending, revision);
        Assert.Contains(action == "approve" ? "Reservation Approved" : "Reservation Rejected",
            await Client.GetStringAsync(path + Uri.EscapeDataString(fresh)), StringComparison.Ordinal);
        Assert.Equal(action == "approve" ? ReservationStatus.Confirmed : ReservationStatus.Cancelled, await StatusOfAsync(id));
    }

    private async Task<Guid> SeedReservationAsync(DateTime createdAt)
    {
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Reservations.Add(new Reservation
        {
            Id = id,
            CustomerId = null,
            CustomerName = "Ada Lovelace",
            CustomerEmail = "ada@example.com",
            CustomerPhone = "+41791112233",
            TableId = TableId,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            // The anchor under test. ApplicationDbContext only stamps CreatedAt when it is default.
            CreatedAt = createdAt,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();

        return id;
    }

    private async Task<ReservationStatus> StatusOfAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id)).Status;
    }
}
