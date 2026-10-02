using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class TableGuestAccountReadConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly DatabaseFixture _fixture;

    public TableGuestAccountReadConcurrencyTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Close_can_revoke_a_visit_while_the_guest_bill_is_assembling()
    {
        var visit = await SeedVisitAsync();
        var assemblyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishAssembly = new TaskCompletionSource<TableBillDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerTask = StartRead(visit, assemblyStarted, finishAssembly);
        await assemblyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var closeTask = Task.Run(async () =>
        {
            await using var context = _fixture.CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);
            var session = await TableServiceSessionRowLock.LoadAsync(
                context, visit.SessionId, CancellationToken.None);
            session.Should().NotBeNull();
            session!.Status = TableServiceSessionStatus.Closed;
            var participant = await context.TableGuestParticipants.SingleAsync(
                value => value.ServiceSessionId == visit.SessionId);
            participant.RevokedAt = FixedNow.UtcDateTime;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        var closeCompletedDuringAssembly = await Task.WhenAny(
            closeTask, Task.Delay(TimeSpan.FromSeconds(3))) == closeTask;
        finishAssembly.TrySetResult(null);
        var readError = await Record.ExceptionAsync(async () => await readerTask);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(10));

        closeCompletedDuringAssembly.Should().BeTrue(
            "the account snapshot must not hold the visit row lock while the bill is assembled");
        readError.Should().BeOfType<NotFoundException>()
            .Which.Message.Should().Be("Guest access is unavailable for this table visit.");
    }

    [Fact]
    public async Task Account_read_can_finish_while_a_guest_round_holds_the_visit_lock()
    {
        var visit = await SeedVisitAsync();
        var assemblyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishAssembly = new TaskCompletionSource<TableBillDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerTask = StartRead(visit, assemblyStarted, finishAssembly);
        await assemblyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var roundPrepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var roundTask = Task.Run(async () =>
        {
            await using var context = _fixture.CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);
            var projector = Mock.Of<IOrderResponseProjector>();
            var store = new TableGuestRoundOperationStore(
                context, GuestFeatures(), projector, new FixedTimeProvider(FixedNow));
            var preparation = await store.PrepareUnderLockAsync(
                RoundContext(visit), CancellationToken.None);
            preparation.Participant.Should().NotBeNull();
            roundPrepared.TrySetResult();
            await releaseRound.Task;
            await transaction.CommitAsync();
        });

        var roundAcquiredVisitLock = await Task.WhenAny(
            roundPrepared.Task, Task.Delay(TimeSpan.FromSeconds(3))) == roundPrepared.Task;
        finishAssembly.TrySetResult(SnapshotBill(visit.SessionId));
        var readFinishedWhileRoundHeldLock = await Task.WhenAny(
            readerTask, Task.Delay(TimeSpan.FromSeconds(3))) == readerTask;
        if (!readFinishedWhileRoundHeldLock)
        {
            releaseRound.TrySetResult();
        }

        TableGuestAccountDto? account = null;
        var readError = await Record.ExceptionAsync(async () => account = await readerTask);
        releaseRound.TrySetResult();
        await roundTask.WaitAsync(TimeSpan.FromSeconds(10));

        roundAcquiredVisitLock.Should().BeTrue(
            "the account read must not hold a conflicting row lock while assembling its snapshot");
        readFinishedWhileRoundHeldLock.Should().BeTrue(
            "the post-snapshot authorization check is a normal read, even while a round owns the visit lock");
        readError.Should().BeNull();
        account.Should().NotBeNull();
        account!.ServiceSessionId.Should().Be(visit.SessionId);
        account.AccountRevision.Should().Be(1);
    }

    private async Task<TableGuestAccountDto> StartRead(
        Visit visit,
        TaskCompletionSource assemblyStarted,
        TaskCompletionSource<TableBillDto?> finishAssembly)
    {
        var bills = new Mock<ITableBillAssembler>();
        bills.Setup(value => value.AssembleAsync(visit.SessionId, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken cancellationToken) =>
                WaitForSnapshotAsync(assemblyStarted, finishAssembly, cancellationToken));
        await using var context = _fixture.CreateContext();
        var reader = new TableGuestAccountReader(
            context, GuestFeatures(), bills.Object, new FixedTimeProvider(FixedNow));
        return await reader.ReadAsync(visit.SessionId, visit.Token, CancellationToken.None);
    }

    private static async Task<TableBillDto?> WaitForSnapshotAsync(
        TaskCompletionSource assemblyStarted,
        TaskCompletionSource<TableBillDto?> finishAssembly,
        CancellationToken cancellationToken)
    {
        assemblyStarted.TrySetResult();
        return await finishAssembly.Task.WaitAsync(cancellationToken);
    }

    private async Task<Visit> SeedVisitAsync()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash).Should().BeTrue();

        await using var context = _fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "T-READ",
            QRCodeData = $"qr-{Guid.NewGuid():N}",
            IsActive = true,
            MaxGuests = 4,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestAccountReadConcurrencyTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            TableNumber = 1,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestAccountReadConcurrencyTests),
        });
        context.TableGuestAdmissions.Add(new TableGuestAdmission
        {
            Id = admissionId,
            ServiceSessionId = sessionId,
            CodeHash = TableGuestCredentialCrypto.HashAdmissionCode("123456789A"),
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestAccountReadConcurrencyTests),
        });
        context.TableGuestParticipants.Add(new TableGuestParticipant
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            AdmissionId = admissionId,
            TokenHash = tokenHash,
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestAccountReadConcurrencyTests),
        });
        await context.SaveChangesAsync();
        return new Visit(sessionId, token, tokenHash);
    }

    private static TableBillDto SnapshotBill(Guid sessionId) => new()
    {
        ServiceSessionId = sessionId,
        AccountRevision = 1,
        TableLabel = "T-READ",
        Currency = "CHF",
    };

    private static TableGuestRoundContext RoundContext(Visit visit) => new(
        visit.SessionId, Guid.NewGuid(), 1, visit.TokenHash,
        TableGuestCredentialCrypto.HashBasketSession("round-basket"), new string('A', 64));

    private static ITenantFeatures GuestFeatures() =>
        Mock.Of<ITenantFeatures>(features => features.TableGuestVisitsV1);

    private sealed record Visit(Guid SessionId, string Token, string TokenHash);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
