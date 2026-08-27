using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// The mails a guest's own edit of their booking sends (backend #407): M16 to the guest, and M17
/// to the restaurant when the booked SHAPE moved.
/// </summary>
/// <remarks>
/// Asserted against a recording <see cref="IEmailService"/> and driven over HTTP, because the
/// claim is about the WIRING — which mail fires for which edit, to which address, in which
/// language — and not about the rendered copy (the golden snapshots own that).
/// <para>
/// The rule this class exists to pin is the negative one: a contact-detail fix must NOT alert the
/// restaurant. The alert is a request for a decision, nothing about the decision changed, and an
/// operator who learns that this mail sometimes means nothing stops opening the one that means
/// something.
/// </para>
/// </remarks>
[Collection("Database Lane 2")]
public class ReservationChangedMailTests : IntegrationTestBase
{
    private static readonly Guid MyTableId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid CallerId = Guid.Parse(TestAuthHandler.UserId);
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    private const string BookedDay = "2030-05-17T00:00:00Z";
    private const string MovedDay = "2030-05-18T00:00:00Z";
    private const string GuestEmail = "grace@example.com";

    private readonly Mock<IEmailService> _email = new();

    public ReservationChangedMailTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private string AdminEmail =>
        Factory.Services.GetRequiredService<IOptions<EmailSettings>>().Value.AdminEmail;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Tables.Add(new Table { Id = MyTableId, TableNumber = "T-MINE", MaxGuests = 4, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reshaping_a_confirmed_booking_mails_the_guest_and_asks_the_restaurant_again()
    {
        var id = await SeedReservationAsync(ReservationStatus.Confirmed);

        await EditAsync(id, Body(date: MovedDay, guests: 4));

        _email.Verify(e => e.SendReservationChangedEmailAsync(
                It.IsAny<CultureInfo>(),
                GuestEmail,
                It.IsAny<string>(),
                It.IsAny<ReservationMailDetails>(),
                ReservationChangeOutcome.NeedsApprovalAgain),
            Times.Once(),
            "the guest held a confirmation that no longer applies, and must be told so");

        // Signed, not bare (backend #402): the assertion is on the ?token= the MAILER minted, so a
        // send site that forgot to mint one fails here even though the template still renders a
        // button. The template-level rule lives in ReservationAlertLinkTests.
        var alert = AdminAlert(id);
        alert.Should().MatchRegex($@"/api/Reservations/{id}/quick-approve\?token=[^'&]+");
        alert.Should().MatchRegex($@"/api/Reservations/{id}/quick-reject\?token=[^'&]+");
    }

    [Fact]
    public async Task Reshaping_a_pending_booking_still_replaces_the_alert_the_restaurant_holds()
    {
        var id = await SeedReservationAsync(ReservationStatus.Pending);

        await EditAsync(id, Body(date: MovedDay, guests: 4));

        // Not NeedsApprovalAgain: nothing was withdrawn, the wait simply continues with new numbers.
        _email.Verify(e => e.SendReservationChangedEmailAsync(
                It.IsAny<CultureInfo>(), GuestEmail, It.IsAny<string>(), It.IsAny<ReservationMailDetails>(),
                ReservationChangeOutcome.AwaitingApproval),
            Times.Once());

        // The restaurant is still told: the alert already in its inbox describes a slot that no
        // longer exists, and its buttons now decide these new details.
        _email.Verify(e => e.SendEmailAsync(
                AdminEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task A_contact_only_edit_tells_the_guest_and_leaves_the_restaurant_alone()
    {
        var id = await SeedReservationAsync(ReservationStatus.Confirmed);

        await EditAsync(id, Body(name: "Grace B. Hopper"));

        _email.Verify(e => e.SendReservationChangedEmailAsync(
                It.IsAny<CultureInfo>(), GuestEmail, It.IsAny<string>(), It.IsAny<ReservationMailDetails>(),
                ReservationChangeOutcome.StillConfirmed),
            Times.Once(),
            "the guest gets the written record of what they changed");

        _email.Verify(e => e.SendEmailAsync(
                AdminEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never(),
            "no decision changed, so the restaurant is not asked to take one");
    }

    [Fact]
    public async Task The_guest_mail_is_written_in_the_language_frozen_on_the_booking()
    {
        var id = await SeedReservationAsync(ReservationStatus.Confirmed, language: "fr");

        await EditAsync(id, Body(date: MovedDay, guests: 4));

        _email.Verify(e => e.SendReservationChangedEmailAsync(
                French, GuestEmail, It.IsAny<string>(), It.IsAny<ReservationMailDetails>(),
                It.IsAny<ReservationChangeOutcome>()),
            Times.Once(),
            "the booking's own language is the only one this send path has — there is no request "
            + "language on a mail the restaurant may read hours later");
    }

    [Fact]
    public async Task A_mail_sender_that_throws_does_not_fail_the_update()
    {
        _email.Setup(e => e.SendReservationChangedEmailAsync(
                It.IsAny<CultureInfo>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ReservationMailDetails>(), It.IsAny<ReservationChangeOutcome>()))
            .ThrowsAsync(new InvalidOperationException("the provider is having a bad minute"));

        var id = await SeedReservationAsync(ReservationStatus.Confirmed);

        await EditAsync(id, Body(date: MovedDay, guests: 4));

        var saved = await LoadAsync(id);
        saved.ReservationDate.Date.Should().Be(new DateTime(2030, 5, 18));
        saved.NumberOfGuests.Should().Be(4);
        saved.Status.Should().Be(ReservationStatus.Pending);
    }

    /// <summary>The HTML body of the single alert sent to the admin address.</summary>
    private string AdminAlert(Guid reservationId)
    {
        var sends = _email.Invocations
            .Where(i => i.Method.Name == nameof(IEmailService.SendEmailAsync)
                && string.Equals((string)i.Arguments[0], AdminEmail, StringComparison.Ordinal))
            .ToList();

        sends.Should().ContainSingle("reservation {0} changed shape exactly once", reservationId);
        return (string)sends[0].Arguments[2];
    }

    private async Task EditAsync(Guid id, object body)
    {
        AuthenticateAsUser();
        var response = await PutAsJsonAsync($"/api/Reservations/{id}/mine", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static object Body(
        string date = BookedDay, int guests = 2, string name = "Grace Hopper") => new
        {
            customerName = name,
            customerEmail = GuestEmail,
            customerPhone = "+41794445566",
            reservationDate = date,
            startTime = "19:00:00",
            endTime = "21:00:00",
            numberOfGuests = guests,
        };

    private async Task<Guid> SeedReservationAsync(ReservationStatus status, string? language = null)
    {
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Reservations.Add(new Reservation
        {
            Id = id,
            CustomerId = CallerId,
            CustomerName = "Grace Hopper",
            CustomerEmail = GuestEmail,
            CustomerPhone = "+41794445566",
            TableId = MyTableId,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = status,
            PreferredLanguage = language,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();

        return id;
    }

    private async Task<Reservation> LoadAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }
}
