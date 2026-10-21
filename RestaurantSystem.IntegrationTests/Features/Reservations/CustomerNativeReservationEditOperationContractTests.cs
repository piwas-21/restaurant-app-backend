using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Fact]
    public async Task Preparation_reviews_fresh_original_and_proposal_without_writing_or_exposing_private_authority()
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        var original = await db.Reservations.AsNoTracking().SingleAsync();
        var (_, review) = await PrepareAsync();
        Assert.Equal(_booking, review.Original.ReservationId); Assert.Equal(_booking, review.Proposed.ReservationId);
        Assert.Equal(DateOnly.FromDateTime(Day), review.Original.ReservationDate);
        Assert.Equal(TimeSpan.FromHours(18), review.Original.StartTime); Assert.Equal(TimeSpan.FromHours(20), review.Proposed.StartTime);
        Assert.Equal(ReservationStatus.Confirmed, review.Original.Status); Assert.Equal(ReservationStatus.Pending, review.Proposed.Status);
        Assert.Equal("Europe/Zurich", review.TimeZone); Assert.Equal(Tenant, review.Proof.TenantId);
        var serialized = JsonSerializer.Serialize(review, JsonOptions);
        foreach (var forbidden in new[] { "customerEmail", "customerPhone", "customerName", "tableId", "revision", "editor-staff-note", "private-qr" })
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        await AssertUntouchedAsync(original.Revision);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_edit_preserves_identity_tables_staff_fields_and_confirmation_rules_and_mails_only_after_commit(bool contactOnly)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        var before = await db.Reservations.AsNoTracking().SingleAsync();
        var input = contactOnly ? Request() with { StartTime = TimeSpan.FromHours(18), EndTime = TimeSpan.FromHours(20) } : Request();
        var (request, review) = await PrepareAsync(input); var observed = false;
        _onMail = async (row, edit) =>
        {
            await using var independent = DatabaseFixture.CreateContext();
            var committed = await independent.Reservations.AsNoTracking().SingleAsync();
            Assert.Equal(request.OperationId, (await independent.Set<CustomerReservationOperation>().SingleAsync()).OperationId);
            Assert.Equal("changed@example.test", committed.CustomerEmail); Assert.Equal(request.StartTime, committed.StartTime);
            Assert.Equal(!contactOnly, edit.ShapeChanged); Assert.True(edit.WasConfirmed);
            Assert.Equal(committed.Revision, row.Revision);
            Assert.Equal(row.Status, committed.Status); observed = true;
        };
        var accepted = await DecideAsync("submit", request);
        Assert.Equal("Applied", accepted.GetProperty("outcome").GetString());
        Assert.Equal(_booking, accepted.GetProperty("reservationId").GetGuid());
        Assert.True(observed); Assert.Equal(1, _mailCalls);
        var after = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(contactOnly ? ReservationStatus.Confirmed : ReservationStatus.Pending, after.Status);
        Assert.NotEqual(before.Revision, after.Revision); Assert.Equal(_first, after.TableId);
        Assert.Equal(_second, (await db.Set<ReservationTable>().SingleAsync()).TableId);
        Assert.Equal(before.Notes, after.Notes); Assert.Equal(before.CreatedAt, after.CreatedAt); Assert.Equal(before.CreatedBy, after.CreatedBy);
        Assert.Equal(before.PreferredLanguage, after.PreferredLanguage); Assert.Equal(Owner, after.CustomerId);
        Assert.Equal(Day, after.ReservationDate); Assert.Equal("Changed request", after.SpecialRequests);
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("recover", review.Proof)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("submit", request)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("cancel-attempt", review.Proof)).GetRawText()); Assert.Equal(1, _mailCalls);
    }
    [Fact]
    public async Task Unseen_is_read_only_and_explicit_stopping_seals_only_the_unreceived_attempt()
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext(); var revision = (await db.Reservations.SingleAsync()).Revision;
        var (request, review) = await PrepareAsync();
        Assert.Equal("Unseen", (await DecideAsync("recover", review.Proof)).GetProperty("outcome").GetString());
        await AssertUntouchedAsync(revision);
        var stopped = await DecideAsync("cancel-attempt", review.Proof);
        Assert.Equal("Cancelled", stopped.GetProperty("outcome").GetString()); Assert.Equal(JsonValueKind.Null, stopped.GetProperty("reservationId").ValueKind);
        Assert.Equal(stopped.GetRawText(), (await DecideAsync("submit", request)).GetRawText());
        Assert.Equal(revision, (await db.Reservations.AsNoTracking().SingleAsync()).Revision);
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("requests")]
    [InlineData("date")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("party")]
    [InlineData("booking")]
    public async Task Prepared_edit_binds_every_input_field_before_an_effect_or_receipt(string mode)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext(); var revision = (await db.Reservations.SingleAsync()).Revision;
        var (request, _) = await PrepareAsync();
        var changed = mode switch
        {
            "name" => request with { CustomerName = "Another name" },
            "email" => request with { CustomerEmail = "another@example.test" },
            "phone" => request with { CustomerPhone = null },
            "requests" => request with { SpecialRequests = null },
            "date" => request with { Date = request.Date!.Value.AddDays(7) },
            "start" => request with { StartTime = TimeSpan.FromHours(20.5) },
            "end" => request with { EndTime = TimeSpan.FromHours(22.5) },
            "party" => request with { NumberOfGuests = 5 },
            _ => request with { ReservationId = Guid.NewGuid() }
        };
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", changed)).StatusCode); await AssertUntouchedAsync(revision);
    }
    [Theory]
    [InlineData("status")]
    [InlineData("tableId")]
    [InlineData("notes")]
    [InlineData("customerId")]
    public async Task Native_edit_never_accepts_staff_authority_fields(string field)
    {
        Authenticate(); var data = JsonSerializer.SerializeToNode(Request(), JsonOptions)!.AsObject();
        data[field] = field == "status" ? "Confirmed" : Guid.NewGuid().ToString("D");
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("prepare", data)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }
}
