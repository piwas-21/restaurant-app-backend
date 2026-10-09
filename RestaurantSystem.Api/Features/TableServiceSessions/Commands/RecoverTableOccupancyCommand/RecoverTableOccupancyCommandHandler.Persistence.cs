using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed partial class RecoverTableOccupancyCommandHandler
{
    private async Task<ApiResponse<TableOccupancyRecoveryOperationDto>?> ReplayIfPresentAsync(
        Guid tableId, RecoverTableOccupancyCommand command, string requestHash,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var operation = await context.TableOccupancyRecoveryOperations
            .SingleOrDefaultAsync(value => value.Id == command.OperationId, cancellationToken);
        if (operation is null) return null;
        if (operation.TableId != tableId || operation.ActorUserId != currentUser.UserId
            || operation.ActorRole != currentUser.Role || operation.RequestHash != requestHash)
        {
            return Failure("This operation id was already used for a different recovery request.",
                ErrorCodes.TableOccupancyRecoveryOperationMismatch);
        }

        var dispositions = await context.TableOccupancyRecoveryDispositions.AsNoTracking()
            .Where(value => value.OperationId == operation.Id)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApiResponse<TableOccupancyRecoveryOperationDto>.SuccessWithData(
            TableOccupancyRecoveryResponses.ToDto(operation, dispositions),
            "Table occupancy recovery result retrieved");
    }

    private async Task<LockedRecoveryResult> RecoverLockedAsync(
        Table table,
        RecoverTableOccupancyCommand command,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (!table.IsActive)
            return LockedRecoveryResult.Failed(Failure(
                "The selected table is inactive.", ErrorCodes.TableServiceTableInactive));
        if (table.ReadinessVersion != command.ExpectedReadinessVersion)
            return LockedRecoveryResult.Failed(StalePreview());

        var sessionResult = await ResolveLockedSessionAsync(table, command, cancellationToken);
        if (sessionResult.Error is not null)
            return LockedRecoveryResult.Failed(sessionResult.Error);
        var session = sessionResult.Session;
        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        if (await TableReadinessLegacyRules.HasAmbiguousLegacyOpenVisitAsync(
                context, tableNumber, cancellationToken, session?.Id))
        {
            return LockedRecoveryResult.Failed(Failure(
                "Resolve the unassigned legacy visit identity before recovering this table.",
                ErrorCodes.TableServiceSessionAmbiguous));
        }

        var snapshot = await TableOccupancyRecoverySnapshot.ReadAsync(
            context, table, session, settings.Value.PaymentTolerance, cancellationToken);
        if (!string.Equals(snapshot.PreviewFingerprint, command.PreviewFingerprint,
                StringComparison.OrdinalIgnoreCase))
            return LockedRecoveryResult.Failed(StalePreview());

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var now = new DateTime(
            utcNow.Ticks / TimeSpan.TicksPerMicrosecond * TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc);
        var audit = currentUser.GetAuditIdentifier();
        var cancelledMemberOrders = 0;
        var dispositions = new List<TableOccupancyRecoveryDisposition>(snapshot.Orders.Count);
        foreach (var order in snapshot.Orders)
        {
            var kind = snapshot.DispositionFor(order);
            dispositions.Add(CreateDisposition(
                snapshot, order, kind, command.OperationId, now, audit));
            if (kind == TableOccupancyRecoveryDispositionKind.CancelledUnsent)
            {
                CancelUnsentOrder(order, command.OperationId, now, audit);
                if (session is not null && order.ServiceSessionId == session.Id)
                    cancelledMemberOrders++;
            }
        }

        if (session is not null)
            await ReleaseVisitAsync(session, cancelledMemberOrders, now, audit, cancellationToken);

        table.ReadinessState = TableReadinessState.NeedsReset;
        table.ReadinessVersion = checked(table.ReadinessVersion + 1);
        var operation = CreateOperation(table, session, command, requestHash, now, audit);
        context.TableOccupancyRecoveryOperations.Add(operation);
        context.TableOccupancyRecoveryDispositions.AddRange(dispositions);
        return LockedRecoveryResult.Completed(
            TableOccupancyRecoveryResponses.ToDto(operation, dispositions));
    }

    private async Task ReleaseVisitAsync(
        TableServiceSession session, int cancelledOrderCount, DateTime now, string audit,
        CancellationToken cancellationToken)
    {
        var newlyReleased = !session.ReleasedAt.HasValue;
        if (newlyReleased)
        {
            await guestVisits.RevokeForSessionAsync(session.Id, now, cancellationToken);
            session.ReleasedAt = now;
            session.ReleasedBy = audit;
        }

        if (cancelledOrderCount > 0)
            session.RecordAccountChange();
        else if (newlyReleased)
            session.Version++;
    }

    private TableOccupancyRecoveryOperation CreateOperation(
        Table table, TableServiceSession? session, RecoverTableOccupancyCommand command,
        string requestHash, DateTime now, string audit) => new()
        {
            Id = command.OperationId,
            TableId = table.Id,
            ServiceSessionId = session?.Id,
            ActorUserId = currentUser.UserId.GetValueOrDefault(),
            ActorRole = currentUser.Role.GetValueOrDefault(),
            RequestHash = requestHash,
            PreviewFingerprint = command.PreviewFingerprint.ToUpperInvariant(),
            Reason = command.Reason.Trim(),
            ExpectedReadinessVersion = command.ExpectedReadinessVersion,
            OutcomeReadinessVersion = table.ReadinessVersion,
            OutcomeReadinessState = table.ReadinessState,
            ExpectedSessionVersion = command.ExpectedSessionVersion,
            ExpectedAccountRevision = command.ExpectedAccountRevision,
            OutcomeSessionVersion = session?.Version,
            OutcomeAccountRevision = session?.AccountRevision,
            VisitReleasedAt = session?.ReleasedAt,
            RecordedAt = now,
            CreatedAt = now,
            CreatedBy = audit
        };

    private static ApiResponse<TableOccupancyRecoveryOperationDto> StalePreview() => Failure(
        "The table or visit changed after preview. Refresh the preview and confirm again.",
        ErrorCodes.TableOccupancyRecoveryPreviewStale);

    private sealed record LockedRecoveryResult(
        TableOccupancyRecoveryOperationDto? Operation,
        ApiResponse<TableOccupancyRecoveryOperationDto>? Error)
    {
        public static LockedRecoveryResult Completed(TableOccupancyRecoveryOperationDto operation) =>
            new(operation, null);
        public static LockedRecoveryResult Failed(ApiResponse<TableOccupancyRecoveryOperationDto> error) =>
            new(null, error);
    }

}
