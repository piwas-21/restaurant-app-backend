using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.UpdateMyReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.UpdateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

[Collection("Database Lane 2")]
public sealed partial class ReservationWriteConcurrencyTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly DateTime Day = new(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc);
    private readonly Guid _table = Guid.NewGuid();
    private readonly Guid _otherTable = Guid.NewGuid();
    private readonly Guid _existing = Guid.NewGuid();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var db = DatabaseFixture.CreateContext();
        db.Tables.AddRange(new Table { Id = _table, TableNumber = "LOCK-A", MaxGuests = 4, CreatedBy = "test" },
            new Table { Id = _otherTable, TableNumber = "LOCK-B", MaxGuests = 4, CreatedBy = "test" });
        db.Reservations.Add(new Reservation
        {
            Id = _existing,
            CustomerId = Owner,
            CustomerName = nameof(ReservationWriteConcurrencyTests),
            CustomerEmail = TestAuthHandler.UserName,
            TableId = _table,
            ReservationDate = Day.AddDays(1),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("admin-edit")]
    [InlineData("owner-edit")]
    public async Task Overlapping_create_and_another_writer_cannot_both_claim_the_table(string writer)
    {
        var pause = new PauseAfterConflict();
        var observe = new ObserveScheduleLock();
        await using var firstDb = DatabaseFixture.CreateContext(pause);
        await using var secondDb = DatabaseFixture.CreateContext(observe);
        using var scope = Factory.Services.CreateScope();
        var first = Creator(scope.ServiceProvider, firstDb)
            .Handle(new(Payload()), default);
        Task<bool>? second = null;
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            second = RunWriterAsync(writer, secondDb);
            var pid = await observe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await AssertWaitingForScheduleAsync(pid, second);
        }
        finally
        {
            pause.Release.TrySetResult();
            await first;
            if (second is not null) await second;
        }
        Assert.True((await first).Success);
        Assert.NotNull(second);
        Assert.False(await second);
        await using var verify = DatabaseFixture.CreateContext();
        Assert.Equal(1, await verify.Reservations.CountAsync(row => row.TableId == _table && row.ReservationDate == Day));
        Assert.Equal(Day.AddDays(1), (await verify.Reservations.SingleAsync(row => row.Id == _existing)).ReservationDate);
    }

    private static CreateReservationCommandHandler Creator(IServiceProvider services, ApplicationDbContext db,
        IReservationCreatedMailer? mailer = null)
    {
        var writer = ActivatorUtilities.CreateInstance<ReservationDraftWriter>(services, db);
        return mailer is null
            ? ActivatorUtilities.CreateInstance<CreateReservationCommandHandler>(services, db, writer)
            : ActivatorUtilities.CreateInstance<CreateReservationCommandHandler>(services, db, writer, mailer);
    }

    private async Task<bool> RunWriterAsync(string writer, ApplicationDbContext db)
    {
        using var scope = Factory.Services.CreateScope();
        if (writer == "create")
            return (await Creator(scope.ServiceProvider, db)
                .Handle(new(Payload()), default)).Success;
        if (writer == "admin-edit")
            return (await ActivatorUtilities.CreateInstance<UpdateReservationCommandHandler>(scope.ServiceProvider, db)
                .Handle(new(_existing, new UpdateReservationDto
                {
                    CustomerName = nameof(ReservationWriteConcurrencyTests),
                    CustomerEmail = TestAuthHandler.UserName,
                    TableId = _table,
                    ReservationDate = Day,
                    StartTime = TimeSpan.FromHours(18),
                    EndTime = TimeSpan.FromHours(20),
                    NumberOfGuests = 2,
                    Status = ReservationStatus.Pending
                }), default)).Success;
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.UserId).Returns(Owner);
        try
        {
            return (await ActivatorUtilities.CreateInstance<UpdateMyReservationCommandHandler>(scope.ServiceProvider, db, actor.Object)
                .Handle(new(_existing, new UpdateMyReservationDto
                {
                    CustomerName = nameof(ReservationWriteConcurrencyTests),
                    CustomerEmail = TestAuthHandler.UserName,
                    ReservationDate = Day,
                    StartTime = TimeSpan.FromHours(18),
                    EndTime = TimeSpan.FromHours(20),
                    NumberOfGuests = 2
                }), default)).Success;
        }
        catch (BadRequestException) { return false; }
    }

    private CreateReservationDto Payload() => new()
    {
        CustomerName = nameof(ReservationWriteConcurrencyTests),
        CustomerEmail = TestAuthHandler.UserName,
        TableId = _table,
        ReservationDate = Day,
        StartTime = TimeSpan.FromHours(18),
        EndTime = TimeSpan.FromHours(20),
        NumberOfGuests = 2
    };

    private async Task AssertWaitingForScheduleAsync(int pid, Task operation)
    {
        await using var observer = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await observer.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE pid = @pid AND locktype = 'advisory' AND NOT granted)", observer);
            command.Parameters.AddWithValue("pid", pid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true) return;
            Assert.False(operation.IsCompleted, "The second writer finished before the first booking committed.");
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class ObserveScheduleLock : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("advisory_xact_lock", StringComparison.Ordinal))
                Entered.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseAfterConflict : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("EXISTS", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Reservations\"", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
