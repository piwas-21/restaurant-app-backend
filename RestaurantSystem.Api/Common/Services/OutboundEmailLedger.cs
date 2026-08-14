using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <inheritdoc />
public class OutboundEmailLedger : IOutboundEmailLedger
{
    /// <summary>PostgreSQL <c>unique_violation</c>. The claim already exists.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>
    /// The author of a claim. Not <c>ICurrentUserService.GetAuditIdentifier()</c> on purpose: the
    /// whole point of GAP-11 is that these mails are no longer attributable to whoever's browser
    /// happened to be open, and the admin alert is written from a task with no request at all.
    /// </summary>
    private const string ClaimAuthor = "System";

    /// <summary>
    /// A claim whose send never reported back is presumed dead after this long and may be taken
    /// over by the next caller. Comfortably longer than the worst-case send (3 provider attempts,
    /// 1 s apart).
    /// <para>
    /// Note what this does and does not buy: nothing sweeps the table, so a take-over only happens
    /// if something asks for the same mail again later. A process killed between claiming and
    /// sending therefore still loses that mail unless a resend is triggered. Closing that needs the
    /// retry job GAP-1 already calls for; this window is what makes the retry safe, not a retry.
    /// </para>
    /// </summary>
    private const int StaleClaimMinutes = 15;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboundEmailLedger> _logger;

    public OutboundEmailLedger(IServiceScopeFactory scopeFactory, ILogger<OutboundEmailLedger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        try
        {
            db.OutboundEmails.Add(new OutboundEmail
            {
                EmailType = emailType,
                EntityId = entityId,
                CreatedAt = now,
                CreatedBy = ClaimAuthor,
            });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost the race, or the mail went out earlier. Either way this caller must not send —
            // unless the holder died mid-send, which is the one case worth reclaiming.
            db.ChangeTracker.Clear();
            return await TryTakeOverStaleClaimAsync(db, emailType, entityId, now, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;

            await db.OutboundEmails
                .Where(e => e.EmailType == emailType && e.EntityId == entityId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.SentAt, now)
                        .SetProperty(e => e.UpdatedAt, now)
                        // ExecuteUpdate bypasses the audit interceptor, so the author is set here
                        // or the row ends up with a timestamp and no author.
                        .SetProperty(e => e.UpdatedBy, ClaimAuthor),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            // The mail is already with the provider; failing the caller here would be worse than
            // the residual risk (an unmarked claim is reclaimable after StaleClaimMinutes).
            _logger.LogError(ex, "Failed to mark {EmailType} for {EntityId} as sent", emailType, entityId);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // SentAt == null guards the one dangerous case: releasing a claim whose mail DID go out
            // would let the next caller send a duplicate.
            await db.OutboundEmails
                .Where(e => e.EmailType == emailType && e.EntityId == entityId && e.SentAt == null)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release the {EmailType} claim for {EntityId}", emailType, entityId);
        }
    }

    private async Task<bool> TryTakeOverStaleClaimAsync(
        ApplicationDbContext db, string emailType, Guid entityId, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddMinutes(-StaleClaimMinutes);

        // The row count from a single UPDATE is the arbitration: two racing take-overs cannot both
        // see CreatedAt below the cutoff, because the winner moves it forward in the same statement.
        var takenOver = await db.OutboundEmails
            .Where(e => e.EmailType == emailType
                && e.EntityId == entityId
                && e.SentAt == null
                && e.CreatedAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.CreatedAt, now)
                    .SetProperty(e => e.UpdatedAt, now)
                    .SetProperty(e => e.UpdatedBy, ClaimAuthor),
                cancellationToken);

        if (takenOver > 0)
        {
            _logger.LogWarning(
                "Re-claiming a stale {EmailType} claim for {EntityId}: its sender never reported back",
                emailType, entityId);
            return true;
        }

        return false;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
