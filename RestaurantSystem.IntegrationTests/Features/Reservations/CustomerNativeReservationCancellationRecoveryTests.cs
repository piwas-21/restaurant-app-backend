using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCancellationTests
{
    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    public async Task Exact_cancellation_commits_one_effect_and_decision_before_one_mail_and_replay_does_not_reapply(ReservationStatus status)
    {
        Authenticate();
        await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status));
        var prepared = await PrepareAsync(); var proof = prepared.Proof;
        var observed = false;
        _onMail = async reservation =>
        {
            await using var observer = DatabaseFixture.CreateContext();
            observed = await observer.Reservations.AnyAsync(row => row.Id == reservation.Id && row.Status == ReservationStatus.Cancelled)
                && await observer.Set<CustomerReservationOperation>().AnyAsync(row => row.OperationId == proof.OperationId && row.ReservationId == reservation.Id);
            Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        };
        var first = await DecideAsync("submit", proof);
        Assert.True(observed); Assert.Equal("Applied", first.GetProperty("outcome").GetString());
        Assert.Equal(_booking, first.GetProperty("reservationId").GetGuid());
        Assert.Equal(first.GetRawText(), (await DecideAsync("submit", proof)).GetRawText());
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Confirmed));
        Mobile.Enabled = false;
        foreach (var path in new[] { "submit", "recover", "cancel-attempt" })
            Assert.Equal(first.GetRawText(), (await DecideAsync(path, proof)).GetRawText());
        Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.SingleAsync()).Status);
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(1, _mailCalls);
        await db.Reservations.ExecuteDeleteAsync();
        Assert.Equal(first.GetRawText(), (await DecideAsync("recover", proof)).GetRawText());
    }
    [Fact]
    public async Task Preparation_is_read_only_and_returns_fresh_public_booking_fields_without_contacts_or_table_data()
    {
        Authenticate();
        var prepared = await PrepareAsync();
        Assert.Equal(_booking, prepared.Reservation.ReservationId);
        Assert.Equal(new DateOnly(2030, 5, 17), prepared.Reservation.ReservationDate);
        Assert.Equal("Europe/Zurich", prepared.TimeZone); Assert.Equal(ReservationStatus.Confirmed, prepared.Reservation.Status);
        var json = JsonSerializer.Serialize(prepared);
        foreach (var privateText in new[] { "private@example.test", "Private customer", "+41000000000", "operator-private", "CANCEL-A", "private-qr", "OwnerHash", "Revision" })
            Assert.DoesNotContain(privateText, json, StringComparison.OrdinalIgnoreCase);
        await AssertUntouchedAsync();
    }
    [Fact]
    public async Task Unseen_recovery_does_not_release_or_execute_and_explicit_stop_seals_only_the_attempt()
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        Mobile.Enabled = false;
        var unseen = await DecideAsync("recover", proof);
        Assert.Equal("Unseen", unseen.GetProperty("outcome").GetString());
        await AssertUntouchedAsync();
        var stopped = await DecideAsync("cancel-attempt", proof);
        Assert.Equal("Cancelled", stopped.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("reservationId").ValueKind);
        Mobile.Enabled = true;
        Assert.Equal(stopped.GetRawText(), (await DecideAsync("submit", proof)).GetRawText());
        await using var db = DatabaseFixture.CreateContext();
        Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.SingleAsync()).Status);
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("status")]
    [InlineData("raw-contact-aba")]
    [InlineData("table-child-aba")]
    [InlineData("delete-recreate")]
    public async Task Any_booking_revision_change_refuses_the_original_cancellation_without_a_receipt(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        await using var db = DatabaseFixture.CreateContext();
        if (mode == "status")
        {
            await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Cancelled));
            await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Confirmed));
        }
        else if (mode == "raw-contact-aba")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Reservations\" SET customer_phone = 'temporary' WHERE id = {_booking}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Reservations\" SET customer_phone = '+41000000000' WHERE id = {_booking}");
        }
        else if (mode == "table-child-aba")
        {
            var extra = new Table { TableNumber = "CANCEL-B", MaxGuests = 2, CreatedBy = "test" };
            db.Tables.Add(extra); await db.SaveChangesAsync();
            db.Set<ReservationTable>().Add(new ReservationTable { ReservationId = _booking, TableId = extra.Id, CreatedBy = "test" });
            await db.SaveChangesAsync(); await db.Set<ReservationTable>().ExecuteDeleteAsync();
        }
        else
        {
            var original = await db.Reservations.AsNoTracking().SingleAsync();
            await db.Reservations.ExecuteDeleteAsync(); db.Reservations.Add(original); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", proof)).StatusCode);
        Assert.Equal("Unseen", (await DecideAsync("recover", proof)).GetProperty("outcome").GetString());
        await AssertUntouchedAsync();
        Assert.Equal("Cancelled", (await DecideAsync("cancel-attempt", proof)).GetProperty("outcome").GetString());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_submissions_apply_at_most_one_cancellation(bool sameOperation)
    {
        Authenticate(); var first = (await PrepareAsync()).Proof;
        var second = sameOperation ? first : (await PrepareAsync()).Proof;
        var responses = await Task.WhenAll(SendAsync("submit", first), SendAsync("submit", second));
        Assert.Equal(sameOperation ? 2 : 1, responses.Count(value => value.StatusCode == HttpStatusCode.OK));
        Assert.Equal(sameOperation ? 0 : 1, responses.Count(value => value.StatusCode == HttpStatusCode.Conflict));
        await using var db = DatabaseFixture.CreateContext();
        Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(1, _mailCalls);
        foreach (var response in responses) response.Dispose();
    }
}
