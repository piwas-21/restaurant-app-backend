using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class TableGuestParticipantPaymentAuthorizationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Active_participant_resolves_to_typed_non_pii_actor_for_locked_and_read_paths()
    {
        var visit = await SeedVisitAsync();
        await using var context = fixture.CreateContext();
        var authorization = new TableGuestParticipantPaymentAuthorization(context, new FixedTimeProvider(FixedNow));

        var activeActor = await authorization.AuthorizeActiveAsync(
            visit.SessionId, visit.Token, CancellationToken.None);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var lockedSession = await TableServiceSessionRowLock.LoadAsync(
            context, visit.SessionId, CancellationToken.None);
        var lockedActor = await authorization.AuthorizeLockedAsync(
            lockedSession!, visit.Token, CancellationToken.None);

        activeActor.ActorId.Should().Be(visit.ParticipantId);
        activeActor.Kind.Should().Be(AccountPaymentActorKind.GuestParticipant);
        activeActor.ActorId.Should().Be(lockedActor.ActorId);
        activeActor.Kind.Should().Be(lockedActor.Kind);
        activeActor.AuditIdentifier.Should().Contain(visit.ParticipantId.ToString("N"));
        activeActor.AuditIdentifier.Should().NotContain(visit.Token);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Expired_revoked_closed_and_legacy_unstable_visit_credentials_fail_closed()
    {
        var visit = await SeedVisitAsync(includeInactiveParticipants: true);
        await using var context = fixture.CreateContext();
        var authorization = new TableGuestParticipantPaymentAuthorization(context, new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<NotFoundException>(() => authorization.AuthorizeActiveAsync(
            visit.SessionId, visit.ExpiredToken!, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => authorization.AuthorizeActiveAsync(
            visit.SessionId, visit.RevokedToken!, CancellationToken.None));

        await using (var update = fixture.CreateContext())
        {
            var session = await update.TableServiceSessions.SingleAsync(value => value.Id == visit.SessionId);
            session.Status = TableServiceSessionStatus.Closed;
            await update.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<NotFoundException>(() => authorization.AuthorizeActiveAsync(
            visit.SessionId, visit.Token, CancellationToken.None));

        var legacySessionId = Guid.NewGuid();
        await using (var addLegacy = fixture.CreateContext())
        {
            var legacy = new TableServiceSession
            {
                Id = legacySessionId,
                Status = TableServiceSessionStatus.Open,
                Currency = "CHF",
                OpenedAt = FixedNow.UtcDateTime,
                CreatedAt = FixedNow.UtcDateTime,
                CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
            };
            addLegacy.TableServiceSessions.Add(legacy);
            await addLegacy.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<NotFoundException>(() => authorization.AuthorizeActiveAsync(
            legacySessionId, visit.Token, CancellationToken.None));
    }

    [Fact]
    public async Task Close_that_wins_the_visit_lock_prevents_late_participant_authorization()
    {
        var visit = await SeedVisitAsync();
        await using var first = fixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var locked = await TableServiceSessionRowLock.LoadAsync(first, visit.SessionId, CancellationToken.None);
        locked!.Status = TableServiceSessionStatus.Closed;
        var participant = await first.TableGuestParticipants.SingleAsync(value => value.Id == visit.ParticipantId);
        participant.RevokedAt = FixedNow.UtcDateTime;
        await first.SaveChangesAsync();

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateAuthorization = Task.Run(async () =>
        {
            await using var second = fixture.CreateContext();
            await using var secondTransaction = await second.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            secondStarted.SetResult();
            var session = await TableServiceSessionRowLock.LoadAsync(second, visit.SessionId, CancellationToken.None);
            try
            {
                var authorization = new TableGuestParticipantPaymentAuthorization(
                    second, new FixedTimeProvider(FixedNow));
                _ = await authorization.AuthorizeLockedAsync(session!, visit.Token, CancellationToken.None);
                return true;
            }
            catch (NotFoundException)
            {
                return false;
            }
            finally
            {
                await secondTransaction.CommitAsync();
            }
        });

        await secondStarted.Task;
        await firstTransaction.CommitAsync();
        (await lateAuthorization).Should().BeFalse();
    }

    private async Task<SeededVisit> SeedVisitAsync(bool includeInactiveParticipants = false)
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash).Should().BeTrue();
        var expiredToken = includeInactiveParticipants ? TableGuestCredentialCrypto.CreateParticipantToken() : null;
        var revokedToken = includeInactiveParticipants ? TableGuestCredentialCrypto.CreateParticipantToken() : null;
        await using var context = fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "T-PAYAUTH",
            QRCodeData = $"qr-{sessionId:N}",
            IsActive = true,
            MaxGuests = 4,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            Currency = "CHF",
            OpenedAt = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
        });
        var admission = new TableGuestAdmission
        {
            Id = admissionId,
            ServiceSessionId = sessionId,
            CodeHash = TableGuestCredentialCrypto.HashAdmissionCode("123456789A"),
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
        };
        context.TableGuestAdmissions.Add(admission);
        context.TableGuestParticipants.Add(new TableGuestParticipant
        {
            Id = participantId,
            ServiceSessionId = sessionId,
            AdmissionId = admissionId,
            TokenHash = tokenHash,
            ExpiresAt = FixedNow.UtcDateTime.AddHours(1),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
        });
        if (includeInactiveParticipants)
        {
            context.TableGuestParticipants.Add(Participant(
                sessionId, admissionId, expiredToken!, FixedNow.UtcDateTime.AddSeconds(-1), null));
            context.TableGuestParticipants.Add(Participant(
                sessionId, admissionId, revokedToken!, FixedNow.UtcDateTime.AddHours(1), FixedNow.UtcDateTime));
        }
        await context.SaveChangesAsync();
        return new SeededVisit(sessionId, participantId, token, expiredToken, revokedToken);
    }

    private static TableGuestParticipant Participant(
        Guid sessionId, Guid admissionId, string token, DateTime expiresAt, DateTime? revokedAt)
    {
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash).Should().BeTrue();
        return new TableGuestParticipant
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            AdmissionId = admissionId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestParticipantPaymentAuthorizationTests),
        };
    }

    private sealed record SeededVisit(
        Guid SessionId, Guid ParticipantId, string Token, string? ExpiredToken, string? RevokedToken);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
