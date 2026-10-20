using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.DeleteReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class ReservationWriteConcurrencyTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("delete")]
    public async Task Status_and_deletion_writers_wait_then_read_the_committed_booking(string writer)
    {
        await using var holderDb = DatabaseFixture.CreateContext();
        await using var held = await ReservationWriteLock.BeginAsync(holderDb, default);
        var observation = new ObserveScheduleLock();
        await using var writerDb = DatabaseFixture.CreateContext(observation);
        using var scope = Factory.Services.CreateScope();
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.IsStaff).Returns(true);
        var operation = writer switch
        {
            "confirm" => ActivatorUtilities.CreateInstance<ConfirmReservationCommandHandler>(scope.ServiceProvider, writerDb)
                .Handle(new(_existing), default),
            "cancel" => ActivatorUtilities.CreateInstance<CancelReservationCommandHandler>(scope.ServiceProvider, writerDb, actor.Object)
                .Handle(new(_existing), default),
            _ => ActivatorUtilities.CreateInstance<DeleteReservationCommandHandler>(scope.ServiceProvider, writerDb, actor.Object)
                .Handle(new(_existing), default)
        };
        try
        {
            var pid = await observation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await AssertWaitingForScheduleAsync(pid, operation);
            var changed = await holderDb.Reservations.SingleAsync(row => row.Id == _existing);
            changed.Status = ReservationStatus.Completed;
            await holderDb.SaveChangesAsync();
        }
        finally
        {
            await held.CommitAsync();
            await operation;
        }
        var result = await operation;
        Assert.Equal(writer == "delete", result.Success);
        await using var verify = DatabaseFixture.CreateContext();
        var row = await verify.Reservations.SingleOrDefaultAsync(row => row.Id == _existing);
        if (writer == "delete") Assert.Null(row);
        else Assert.Equal(ReservationStatus.Completed, Assert.IsType<Reservation>(row).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivating_a_no_show_checks_its_combined_tables_and_allows_adjacent_times(bool overlap)
    {
        await using var db = DatabaseFixture.CreateContext();
        var row = await db.Reservations.SingleAsync(row => row.Id == _existing);
        row.Status = ReservationStatus.NoShow;
        row.CombinedTables.Add(new ReservationTable { TableId = _otherTable, CreatedBy = "test" });
        db.Reservations.Add(new Reservation
        {
            CustomerName = nameof(ReservationWriteConcurrencyTests),
            CustomerEmail = row.CustomerEmail,
            TableId = _otherTable,
            ReservationDate = row.ReservationDate,
            NumberOfGuests = 2,
            StartTime = overlap ? TimeSpan.FromHours(19) : TimeSpan.FromHours(20),
            EndTime = TimeSpan.FromHours(22),
            Status = ReservationStatus.Confirmed,
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        using var scope = Factory.Services.CreateScope();
        await using var writer = DatabaseFixture.CreateContext();
        var result = await ActivatorUtilities.CreateInstance<ConfirmReservationCommandHandler>(scope.ServiceProvider, writer)
            .Handle(new(_existing), default);
        Assert.Equal(!overlap, result.Success);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(overlap ? ReservationStatus.NoShow : ReservationStatus.Confirmed,
            (await verify.Reservations.SingleAsync(row => row.Id == _existing)).Status);
    }

    [Fact]
    public async Task Creation_mail_runs_only_after_the_booking_is_visible_to_a_separate_connection()
    {
        var mailed = false;
        var mailer = new Mock<IReservationCreatedMailer>();
        mailer.Setup(value => value.SendAsync(It.IsAny<Reservation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Reservation row, string _, CancellationToken cancellationToken) =>
            {
                await using var observer = DatabaseFixture.CreateContext();
                Assert.True(await observer.Reservations.AnyAsync(value => value.Id == row.Id, cancellationToken));
                mailed = true;
            });
        await using var db = DatabaseFixture.CreateContext();
        using var scope = Factory.Services.CreateScope();
        var result = await ActivatorUtilities.CreateInstance<CreateReservationCommandHandler>(scope.ServiceProvider, db, mailer.Object)
            .Handle(new(Payload()), default);
        Assert.True(result.Success);
        Assert.True(mailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_commits_before_rejection_mail_and_provider_failure_does_not_fail_it(bool failMail)
    {
        ReservationStatus? observedStatus = null;
        string? recipient = null;
        string? subject = null;
        string? html = null;
        var email = new Mock<IEmailService>();
        email.Setup(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async (string to, string title, string body, string _) =>
            {
                await using var observer = DatabaseFixture.CreateContext();
                observedStatus = await observer.Reservations.Where(row => row.Id == _existing)
                    .Select(row => row.Status).SingleAsync();
                recipient = to;
                subject = title;
                html = body;
                if (failMail) throw new IOException("Injected mail provider failure after cancellation.");
            });
        using var scope = Factory.Services.CreateScope();
        var mailer = ActivatorUtilities.CreateInstance<ReservationRejectedMailer>(scope.ServiceProvider, email.Object);
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.UserId).Returns(Owner);
        await using var db = DatabaseFixture.CreateContext();
        var result = await ActivatorUtilities.CreateInstance<CancelReservationCommandHandler>(
                scope.ServiceProvider, db, actor.Object, mailer)
            .Handle(new(_existing), default);

        Assert.True(result.Success);
        Assert.Equal(ReservationStatus.Cancelled, observedStatus);
        Assert.Equal(TestAuthHandler.UserName, recipient);
        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains(nameof(ReservationWriteConcurrencyTests), html, StringComparison.Ordinal);
        email.Verify(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(ReservationStatus.Cancelled,
            await verify.Reservations.Where(row => row.Id == _existing).Select(row => row.Status).SingleAsync());
    }

    [Fact]
    public async Task A_failure_after_insert_rolls_back_the_row_and_releases_the_schedule_for_a_new_writer()
    {
        var mailer = new Mock<IReservationCreatedMailer>();
        using var scope = Factory.Services.CreateScope();
        await using var failedDb = DatabaseFixture.CreateContext(new FailAfterSave());
        var failed = await ActivatorUtilities.CreateInstance<CreateReservationCommandHandler>(scope.ServiceProvider, failedDb, mailer.Object)
            .Handle(new(Payload()), default);
        Assert.False(failed.Success);
        await using var nextDb = DatabaseFixture.CreateContext();
        Assert.False(await nextDb.Reservations.AnyAsync(row => row.ReservationDate == Day));
        mailer.Verify(value => value.SendAsync(It.IsAny<Reservation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var next = await ActivatorUtilities.CreateInstance<CreateReservationCommandHandler>(scope.ServiceProvider, nextDb)
            .Handle(new(Payload()), default);
        Assert.True(next.Success);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(1, await verify.Reservations.CountAsync(row => row.ReservationDate == Day));
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new IOException("Injected failure after database insert.");
    }
}
