using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Features.User.Commands.ConfirmAccountDeletionCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCancellationTests
{
    [Fact]
    public async Task Receipt_save_failure_rolls_back_status_and_revision_clears_tracking_and_does_not_send_mail()
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        await using var observer = DatabaseFixture.CreateContext();
        var revision = (await observer.Reservations.AsNoTracking().SingleAsync()).Revision;
        using var scope = Factory.Services.CreateScope();
        await using var db = DatabaseFixture.CreateContext(new FailCancellationSave());
        var service = Service(scope.ServiceProvider, db);
        await Assert.ThrowsAsync<IOException>(() => service.SubmitAsync(_session, proof, default));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(0, await db.SaveChangesAsync());
        Assert.Equal(revision, (await observer.Reservations.AsNoTracking().SingleAsync()).Revision);
        await AssertUntouchedAsync();
        Assert.Equal("Applied", (await DecideAsync("submit", proof)).GetProperty("outcome").GetString());
        Assert.Equal(1, _mailCalls);
    }
    [Fact]
    public async Task Existing_tracked_booking_does_not_hide_a_committed_staff_edit_in_preparation()
    {
        Authenticate();
        using var scope = Factory.Services.CreateScope();
        await using var db = DatabaseFixture.CreateContext();
        var tracked = await db.Reservations.SingleAsync(); var previous = tracked.Revision;
        await using var writer = DatabaseFixture.CreateContext();
        await writer.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.NumberOfGuests, 3));
        var prepared = await Service(scope.ServiceProvider, db).PrepareAsync(_session, Request(), default);
        Assert.Equal(3, prepared.Reservation.NumberOfGuests);
        Assert.Equal(2, tracked.NumberOfGuests); Assert.Equal(previous, tracked.Revision);
        var applied = await Service(scope.ServiceProvider, db).SubmitAsync(_session, prepared.Proof, default);
        Assert.Equal(RestaurantSystem.Api.Features.Reservations.Dtos.CustomerReservationOperationOutcome.Applied, applied.Outcome);
    }
    [Fact]
    public async Task Real_account_erasure_can_finish_while_cancellation_waits_without_a_user_booking_deadlock()
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customer = await manager.FindByIdAsync(Owner.ToString("D")); Assert.NotNull(customer);
        var token = await manager.GenerateUserTokenAsync(customer, "Default", "AccountDeletion");
        var scrubbed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativePid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scrubber = new PausedScrubber(new RetainedCustomerDataScrubber(db), scrubbed, release);
        var handler = ActivatorUtilities.CreateInstance<ConfirmAccountDeletionCommandHandler>(scope.ServiceProvider, db, scrubber);
        var erasure = handler.Handle(new(Owner, token), default);
        Task<HttpResponseMessage>? cancellation = null;
        try
        {
            await scrubbed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _beforeBooking = async current => nativePid.TrySetResult(await current.Database.SqlQuery<int>(
                $"SELECT pg_backend_pid() AS \"Value\"").SingleAsync());
            cancellation = SendAsync("submit", proof);
            var pid = await nativePid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await MigrationTestSafety.WaitForBlockedMigrationAsync(DatabaseFixture.ConnectionString, pid));
            release.TrySetResult();
            Assert.True((await erasure.WaitAsync(TimeSpan.FromSeconds(10))).Success);
            using var response = await cancellation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await using var observer = DatabaseFixture.CreateContext();
            Assert.False(await observer.Users.AnyAsync(row => row.Id == Owner));
            var retained = await observer.Reservations.SingleAsync();
            Assert.Null(retained.CustomerId); Assert.Equal("[erased]", retained.CustomerEmail);
            Assert.Equal(ReservationStatus.Confirmed, retained.Status);
            Assert.Empty(await observer.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
        }
        finally
        {
            release.TrySetResult();
            await erasure.WaitAsync(TimeSpan.FromSeconds(15));
            if (cancellation is not null) (await cancellation.WaitAsync(TimeSpan.FromSeconds(15))).Dispose();
        }
    }
    [Fact]
    public async Task Actual_mail_provider_failure_after_commit_does_not_change_or_replay_the_canonical_decision()
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        _email.Setup(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("Injected provider failure after commit."));
        var accepted = await DecideAsync("submit", proof);
        Assert.Equal("Applied", accepted.GetProperty("outcome").GetString());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("recover", proof)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("submit", proof)).GetRawText());
        _email.Verify(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Equal(1, _mailCalls);
    }
    private static CustomerReservationCancellationService Service(IServiceProvider services, ApplicationDbContext db)
    {
        var customer = new Mock<ICurrentUserService>();
        customer.SetupGet(value => value.IsAuthenticated).Returns(true);
        customer.SetupGet(value => value.UserId).Returns(Owner);
        customer.SetupGet(value => value.Role).Returns(UserRole.Customer);
        customer.Setup(value => value.GetAuditIdentifier()).Returns(Owner.ToString("D"));
        var access = ActivatorUtilities.CreateInstance<CustomerReservationCancellationAccess>(services, db, customer.Object);
        return ActivatorUtilities.CreateInstance<CustomerReservationCancellationService>(services, db, access);
    }
    private sealed class FailCancellationSave : SaveChangesInterceptor
    {
        private bool _failed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_failed) return ValueTask.FromResult(result);
            _failed = true; throw new IOException("Injected cancellation receipt insert failure.");
        }
    }
    private sealed class PausedScrubber(IRetainedCustomerDataScrubber inner, TaskCompletionSource reached,
        TaskCompletionSource release) : IRetainedCustomerDataScrubber
    {
        public async Task ScrubAsync(Guid customerId, CancellationToken cancellationToken)
        {
            await inner.ScrubAsync(customerId, cancellationToken);
            reached.TrySetResult(); await release.Task.WaitAsync(cancellationToken);
        }
    }
}
