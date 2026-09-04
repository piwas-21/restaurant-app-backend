using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// Backend #420 — <c>Reservation.CustomerPhone</c> is <c>string?</c> on the entity and the COLUMN is
/// nullable in every deployed database (migration <c>20251102031347_MakeCustomerPhoneOptional</c>,
/// which is the only migration to touch it after the table was created), but
/// <c>ReservationConfiguration</c> still mapped it <c>IsRequired()</c>.
///
/// <para>
/// A model that believes a nullable column is non-nullable materialises it with a non-null read, so
/// a single NULL row throws <c>InvalidCastException: Column 'CustomerPhone' is null</c>. The failure
/// is not per-row: <c>GetReservationsQueryHandler</c> catches, so the whole page comes back
/// <c>200 / success:false</c> and ONE phoneless booking hides EVERY reservation from the dashboard.
/// </para>
///
/// <para>
/// <b>It was live.</b> Measured on production 2026-09-04: <c>is_nullable=YES</c> and <b>2 of 19</b>
/// rows in RUMI's <c>Reservations</c> table had a NULL phone. A guest can create one — the create
/// validator has no rule for the field, and its requiredness is per-tenant admin configuration
/// (<c>EnsureRequiredFieldsPresentAsync</c>), so any tenant that has not opted in accepts a booking
/// without a phone.
/// </para>
///
/// <para>
/// The list's own <c>r.CustomerPhone ?? string.Empty</c> does NOT spare it, which is worth stating
/// because it looks as though it should. With the column mapped <c>IsRequired()</c> the expression
/// is non-nullable in the model, so EF's null-semantics pass simplifies the COALESCE away before it
/// ever reaches SQL. Measured on <c>develop</c> with this file copied in: the list test fails with
/// "Failed to retrieve reservations".
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class ReservationNullPhoneTests : IntegrationTestBase
{
    private const string PhonelessName = "#420 phoneless";
    private const string PhonedName = "#420 phoned";

    private Guid _phonelessId;

    public ReservationNullPhoneTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    /// <summary>
    /// The listing, fetched once and asserted successful. Every test here needed both, and
    /// repeating the pair four times was duplication Sonar was right to flag — the version
    /// worth keeping is the one where a failed page cannot reach an assertion about its
    /// contents and surface as a NullReferenceException.
    /// </summary>
    private async Task<PagedResult<ReservationDto>> ListAsync()
    {
        var response = await Client.GetAsync("/api/reservations?pageSize=50");
        var body = await ReadResponseAsync<ApiResponse<PagedResult<ReservationDto>>>(response);

        body!.Success.Should().BeTrue(
            "the reservations page must load before anything can be asserted about it: "
            + string.Join("; ", body.Errors ?? []));
        return body.Data!;
    }

    private async Task<ReservationDto> ListedAsync(string customerName) =>
        (await ListAsync()).Items.Single(r => r.CustomerName == customerName);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var table = new Table { TableNumber = "T-420", MaxGuests = 4, CreatedBy = "test" };
        context.Tables.Add(table);

        // Different sittings on purpose: the admin PUT re-runs the overlap check, and two bookings
        // sharing a table and a slot would make it refuse for a reason unrelated to this issue.
        var phoneless = Booking(PhonelessName, table, null, TimeSpan.FromHours(19));
        context.Reservations.AddRange(
            phoneless, Booking(PhonedName, table, "+41790000000", TimeSpan.FromHours(21)));

        // The insert itself is part of the claim, and it held BEFORE this fix too: EF's
        // `IsRequired()` on a scalar validates nothing client-side, so the nullable column accepted
        // the row on the way in and only threw on the way OUT. That asymmetry is why production
        // accumulated two of these without anyone seeing an error.
        await context.SaveChangesAsync();
        _phonelessId = phoneless.Id;
    }

    private static Reservation Booking(string name, Table table, string? phone, TimeSpan startTime) => new()
    {
        Id = Guid.NewGuid(),
        CustomerName = name,
        CustomerEmail = $"{name.Replace(' ', '-')}@example.com",
        CustomerPhone = phone,
        Table = table,
        ReservationDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        StartTime = startTime,
        EndTime = startTime.Add(TimeSpan.FromHours(1)),
        NumberOfGuests = 2,
        Status = ReservationStatus.Confirmed,
        CreatedBy = "test",
    };

    [Fact]
    public async Task One_phoneless_booking_does_not_hide_every_reservation()
    {
        AuthenticateAsAdmin();

        // `ListAsync` asserts the page succeeded, which IS the defect: before the fix it
        // answered 200 with success:false and "Failed to retrieve reservations".
        var page = await ListAsync();

        page.Items.Select(r => r.CustomerName).Should().Contain([PhonelessName, PhonedName]);
    }

    /// <summary>
    /// The ENTITY itself, materialised through the application's own model — no projection, no
    /// coalesce, nothing between the mapping and the column.
    ///
    /// <para>
    /// This is deliberately not an HTTP test: there is no <c>GET /api/reservations/{id}</c> to
    /// call. But the admin <c>PUT</c>, cancel, confirm, the mail quick-action links and
    /// <c>GuestReservationEdit</c> all load the row as an ENTITY, so this one assertion covers all
    /// of them and keeps covering the next one somebody adds. On <c>develop</c> it fails with
    /// exactly <c>InvalidCastException: Column 'customer_phone' is null</c> from
    /// <c>NpgsqlDataReader.GetString</c> — the fault at its source, one layer below the list's.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_entity_itself_materialises_with_a_null_phone()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var reservation = await context.Reservations.AsNoTracking()
            .FirstAsync(r => r.Id == _phonelessId);

        reservation.CustomerName.Should().Be(PhonelessName);
        reservation.CustomerPhone.Should().BeNull("the column is nullable and the row really is null");
    }

    /// <summary>
    /// The phone still reaches the client as an empty string rather than a null, which is the
    /// contract <c>ReservationDto.CustomerPhone</c> already declares (<c>= string.Empty</c>) and
    /// what both read paths coalesce to. Relaxing the mapping must not change what is SERVED.
    /// </summary>
    [Fact]
    public async Task A_missing_phone_is_served_as_an_empty_string_not_a_null()
    {
        AuthenticateAsAdmin();

        (await ListedAsync(PhonelessName)).CustomerPhone.Should().BeEmpty();
    }

    /// <summary>
    /// Un-hiding the booking is only half the repair: the admin has to be able to SAVE it.
    ///
    /// <para>
    /// The dashboard round-trips what the list served — an empty phone — and
    /// <c>UpdateReservationDto.CustomerPhone</c> carried <c>[Required]</c> on an
    /// <c>[ApiController]</c>, so DataAnnotations refused it before the handler ran. Visible and
    /// uneditable is not an improvement on invisible. Requiredness is now asked of the tenant's own
    /// configuration here, as the create and guest-edit paths already do.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_admin_can_save_the_booking_this_fix_un_hides()
    {
        AuthenticateAsAdmin();

        var listed = await ListedAsync(PhonelessName);

        // Exactly what the dashboard sends back: the values it was served, phone included.
        var response = await PutAsJsonAsync($"/api/reservations/{listed.Id}", new
        {
            customerName = listed.CustomerName,
            customerEmail = listed.CustomerEmail,
            customerPhone = listed.CustomerPhone,
            tableId = listed.TableId,
            reservationDate = listed.ReservationDate,
            startTime = listed.StartTime,
            endTime = listed.EndTime,
            numberOfGuests = listed.NumberOfGuests,
            status = listed.Status.ToString(),
            specialRequests = listed.SpecialRequests,
            notes = "edited by the dashboard",
        });

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var body = await ReadResponseAsync<ApiResponse<ReservationDto>>(response);
        body!.Success.Should().BeTrue(
            "the tenant does not require a phone, so an empty one must not refuse the save: "
            + string.Join("; ", body.Errors ?? []));
        body.Data!.Notes.Should().Be("edited by the dashboard");
    }

    /// <summary>
    /// The control: a booking WITH a phone still carries it. A relaxation that blanked every phone
    /// would satisfy every assertion above.
    /// </summary>
    [Fact]
    public async Task A_booking_with_a_phone_still_carries_it()
    {
        AuthenticateAsAdmin();

        (await ListedAsync(PhonedName)).CustomerPhone.Should().Be("+41790000000");
    }
}
