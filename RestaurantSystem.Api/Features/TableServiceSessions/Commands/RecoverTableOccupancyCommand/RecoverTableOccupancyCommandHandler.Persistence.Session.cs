using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed partial class RecoverTableOccupancyCommandHandler
{
    private async Task<LockedSessionResolution> ResolveLockedSessionAsync(
        Table table, RecoverTableOccupancyCommand command, CancellationToken cancellationToken)
    {
        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        TableServiceSession? session = null;
        if (command.ServiceSessionId is Guid sessionId)
        {
            session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken);
            if (session is null)
                return LockedSessionResolution.Failed(Failure(
                    "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound));
            if (!TableServiceSessionRowLock.IsPhysicalTableResolved(session, table))
                return LockedSessionResolution.Failed(Failure(
                    "The selected visit no longer belongs to this physical table.",
                    ErrorCodes.TableServiceSessionAmbiguous));
            if (session.Status != TableServiceSessionStatus.Open)
                return LockedSessionResolution.Failed(Failure(
                    "A closed table visit cannot be recovered.", ErrorCodes.TableServiceSessionNotClosable));
            if (session.Version != command.ExpectedSessionVersion
                || session.AccountRevision != command.ExpectedAccountRevision)
                return LockedSessionResolution.Failed(StalePreview());
        }
        else if (command.ExpectedSessionVersion.HasValue || command.ExpectedAccountRevision.HasValue)
        {
            return LockedSessionResolution.Failed(StalePreview());
        }

        var activeVisits = await context.TableServiceSessions.AsNoTracking()
            .Where(value => value.Status == TableServiceSessionStatus.Open && value.ReleasedAt == null
                && (value.TableId == table.Id
                    || (!value.TableId.HasValue && tableNumber.HasValue && value.TableNumber == tableNumber)))
            .Select(value => value.Id)
            .ToListAsync(cancellationToken);
        if (activeVisits.Any(value => session is null || value != session.Id))
            return LockedSessionResolution.Failed(Failure(
                "Another open visit is using this physical table. Refresh the recovery preview.",
                ErrorCodes.TableServiceSessionAmbiguous));
        if (session is null && activeVisits.Count != 0)
            return LockedSessionResolution.Failed(StalePreview());

        return LockedSessionResolution.Resolved(session);
    }

    private sealed record LockedSessionResolution(
        TableServiceSession? Session,
        ApiResponse<TableOccupancyRecoveryOperationDto>? Error)
    {
        public static LockedSessionResolution Resolved(TableServiceSession? session) => new(session, null);
        public static LockedSessionResolution Failed(ApiResponse<TableOccupancyRecoveryOperationDto> error) =>
            new(null, error);
    }
}
