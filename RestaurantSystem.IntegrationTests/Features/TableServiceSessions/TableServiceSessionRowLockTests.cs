using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

[Collection("Database Lane 4")]
public sealed class TableServiceSessionRowLockTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableServiceSessionRowLockTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Table_guard_allows_round_order_fk_while_close_waits_for_session()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await SeedVisitAsync(tableId, sessionId);

        await using var closing = _fixture.CreateContext();
        await closing.Database.OpenConnectionAsync();
        await using var closeTransaction = await closing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        (await TableServiceSessionRowLock.LoadTableAsync(
            closing, tableId, CancellationToken.None)).Should().NotBeNull();
        var closeProcessId = ((NpgsqlConnection)closing.Database.GetDbConnection()).ProcessID;

        await using var round = _fixture.CreateContext();
        await using var roundTransaction = await round.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        var lockedSession = await TableServiceSessionRowLock.LoadAsync(
            round, sessionId, CancellationToken.None);
        lockedSession.Should().NotBeNull();

        // Model Close after it has taken the table guard: it now waits for the session row held by
        // the round. The round's Order.TableId FK check must still acquire KEY SHARE on Tables.
        var closeSessionTask = Task.Run(() => TableServiceSessionRowLock.LoadAsync(
            closing, sessionId, CancellationToken.None));
        var closeWaitObserved = await WaitForLockWaitAsync(closeProcessId);

        Exception? roundFailure = null;
        try
        {
            round.Orders.Add(NewRoundOrder(tableId, sessionId));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await round.SaveChangesAsync(timeout.Token);
            await roundTransaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            roundFailure = exception;
            await roundTransaction.RollbackAsync(CancellationToken.None);
        }

        var closingSession = await closeSessionTask.WaitAsync(TimeSpan.FromSeconds(10));
        await closeTransaction.CommitAsync(CancellationToken.None);

        closeWaitObserved.Should().BeTrue("close must be waiting on the session lock held by the round");
        roundFailure.Should().BeNull(
            "a table FK KEY SHARE check must not deadlock with the table lifecycle guard");
        closingSession.Should().NotBeNull();

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.CountAsync(order => order.ServiceSessionId == sessionId
            && order.TableId == tableId)).Should().Be(1);
    }

    private async Task SeedVisitAsync(Guid tableId, Guid sessionId)
    {
        await using var context = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = $"L-{Guid.NewGuid():N}"[..10],
            MaxGuests = 4,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = nameof(TableServiceSessionRowLockTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            TableNumber = 1,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(TableServiceSessionRowLockTests),
        });
        await context.SaveChangesAsync();
    }

    private static Order NewRoundOrder(Guid tableId, Guid sessionId)
    {
        var now = DateTime.UtcNow;
        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"LR-{Guid.NewGuid():N}"[..16],
            Type = OrderType.DineIn,
            TableId = tableId,
            TableNumber = 1,
            ServiceSessionId = sessionId,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(TableServiceSessionRowLockTests),
        };
    }

    private async Task<bool> WaitForLockWaitAsync(int processId)
    {
        await using var observer = _fixture.CreateContext();
        await observer.Database.OpenConnectionAsync();
        await using var command = observer.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "pid";
        parameter.Value = processId;
        command.Parameters.Add(parameter);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is string waitEvent && waitEvent == "Lock")
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }
}
