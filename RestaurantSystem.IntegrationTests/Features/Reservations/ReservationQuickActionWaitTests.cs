using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 4")]
public sealed class ReservationQuickActionWaitTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string RefusalMarker = "This link can no longer be used";
    private readonly Guid _table = Guid.NewGuid();
    private readonly Guid _booking = Guid.NewGuid();
    private readonly MovingClock _clock = new();
    private readonly TaskCompletionSource<QuickActionLinkVerdict> _verified = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
        services.RemoveAll<IReservationQuickActionLinks>();
        services.AddSingleton<IReservationQuickActionLinks>(provider => new ObservedLinks(
            ActivatorUtilities.CreateInstance<ReservationQuickActionLinks>(provider), _verified));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var db = DatabaseFixture.CreateContext();
        db.Tables.Add(new Table { Id = _table, TableNumber = "LINK-WAIT", MaxGuests = 4, CreatedBy = "test" });
        db.Reservations.Add(new Reservation
        {
            Id = _booking,
            TableId = _table,
            CustomerName = nameof(ReservationQuickActionWaitTests),
            CustomerEmail = TestAuthHandler.UserName,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            CreatedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1),
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("approve", "unchanged")]
    [InlineData("reject", "unchanged")]
    [InlineData("approve", "status")]
    [InlineData("reject", "status")]
    [InlineData("approve", "expires")]
    [InlineData("reject", "expires")]
    [InlineData("approve", "deleted")]
    [InlineData("reject", "deleted")]
    [InlineData("approve", "status-aba")]
    [InlineData("reject", "status-aba")]
    [InlineData("approve", "same-values")]
    [InlineData("reject", "same-values")]
    public async Task Quick_action_revalidates_its_original_proof_after_the_real_schedule_wait(
        string action, string change)
    {
        var intent = action == "approve" ? ReservationQuickAction.Approve : ReservationQuickAction.Reject;
        var links = Factory.Services.GetRequiredService<IReservationQuickActionLinks>();
        await using var reader = DatabaseFixture.CreateContext();
        var revision = await reader.Reservations.Where(row => row.Id == _booking).Select(row => row.Revision).SingleAsync();
        var token = links.Mint(_booking, intent, ReservationStatus.Pending, revision);
        AuthenticateAsAnonymous();
        await using var holderDb = DatabaseFixture.CreateContext();
        await using var held = await ReservationWriteLock.BeginAsync(holderDb, default);
        var operation = Client.GetAsync(Link(_booking, action, token));
        var expectedStatus = ReservationStatus.Pending;
        try
        {
            Assert.Equal(QuickActionLinkVerdict.SignatureValid,
                await _verified.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            await AssertWaitingAsync(operation);
            if (change == "expires") _clock.Advance(TimeSpan.FromDays(30));
            if (change == "status")
            {
                expectedStatus = action == "approve" ? ReservationStatus.NoShow : ReservationStatus.Confirmed;
                var row = await holderDb.Reservations.SingleAsync(row => row.Id == _booking);
                row.Status = expectedStatus;
                await holderDb.SaveChangesAsync();
            }
            if (change == "status-aba")
            {
                await holderDb.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Confirmed));
                await holderDb.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Pending));
            }
            if (change == "same-values")
                await holderDb.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.NumberOfGuests, 2));
            if (change == "deleted")
                await holderDb.Reservations.Where(row => row.Id == _booking).ExecuteDeleteAsync();
        }
        finally
        {
            await held.CommitAsync();
            await operation;
        }
        using var response = await operation;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        await using var verify = DatabaseFixture.CreateContext();
        var current = await verify.Reservations.SingleOrDefaultAsync(row => row.Id == _booking);
        if (change == "unchanged")
        {
            Assert.Contains(action == "approve" ? "Reservation Approved" : "Reservation Rejected", html, StringComparison.Ordinal);
            Assert.Equal(action == "approve" ? ReservationStatus.Confirmed : ReservationStatus.Cancelled,
                Assert.IsType<Reservation>(current).Status);
        }
        else
        {
            Assert.Contains(RefusalMarker, html, StringComparison.Ordinal);
            using var unknown = await Client.GetAsync(Link(Guid.NewGuid(), action, token));
            Assert.Equal(await unknown.Content.ReadAsStringAsync(), html);
            if (change == "deleted") Assert.Null(current);
            else Assert.Equal(expectedStatus, Assert.IsType<Reservation>(current).Status);
        }
    }

    private static string Link(Guid id, string action, string? token) =>
        $"/api/reservations/{id:D}/quick-{action}" + (token is null ? string.Empty : "?token=" + Uri.EscapeDataString(token));

    private async Task AssertWaitingAsync(Task operation)
    {
        await using var observer = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await observer.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var query = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND NOT granted
                AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
                """, observer);
            if (await query.ExecuteScalarAsync(timeout.Token) is true) return;
            Assert.False(operation.IsCompleted, "The quick action completed before the schedule holder committed.");
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class MovingClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed class ObservedLinks(IReservationQuickActionLinks inner,
        TaskCompletionSource<QuickActionLinkVerdict> verified) : IReservationQuickActionLinks
    {
        public string Mint(Guid reservationId, ReservationQuickAction action, ReservationStatus status, Guid revision) =>
            inner.Mint(reservationId, action, status, revision);
        public QuickActionLinkVerdict Verify(Guid reservationId, ReservationQuickAction action, ReservationStatus currentStatus,
            Guid currentRevision, string? token)
        {
            var result = inner.Verify(reservationId, action, currentStatus, currentRevision, token);
            verified.TrySetResult(result);
            return result;
        }
    }
}
