using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableOccupancyRecoveryPreviewQuery;

public sealed record GetTableOccupancyRecoveryPreviewQuery(Guid TableId, Guid? ServiceSessionId)
    : IQuery<ApiResponse<TableOccupancyRecoveryPreviewDto>>;

public sealed class GetTableOccupancyRecoveryPreviewQueryHandler(
    ApplicationDbContext context,
    ITenantFeatures features,
    IOptions<TableServiceSessionSettings> settings)
    : IQueryHandler<GetTableOccupancyRecoveryPreviewQuery, ApiResponse<TableOccupancyRecoveryPreviewDto>>
{
    public async Task<ApiResponse<TableOccupancyRecoveryPreviewDto>> Handle(
        GetTableOccupancyRecoveryPreviewQuery query, CancellationToken cancellationToken)
    {
        if (!features.TableVisitReadinessV1)
            return Failure("Explicit table occupancy recovery is not enabled for this tenant.",
                ErrorCodes.TableReadinessFeatureDisabled);

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var table = await context.Tables.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == query.TableId, cancellationToken);
        if (table is null)
            return Failure("The selected table was not found.", ErrorCodes.TableServiceTableNotFound);
        if (!table.IsActive)
            return Failure("The selected table is inactive.", ErrorCodes.TableServiceTableInactive);

        var sessionResult = await ResolveSessionAsync(table, query.ServiceSessionId, cancellationToken);
        if (sessionResult.Error is not null) return sessionResult.Error;

        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        if (await TableReadinessLegacyRules.HasAmbiguousLegacyOpenVisitAsync(
                context, tableNumber, cancellationToken, sessionResult.Session?.Id))
        {
            return Failure("Resolve the unassigned legacy visit identity before recovering this table.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        var snapshot = await TableOccupancyRecoverySnapshot.ReadAsync(
            context, table, sessionResult.Session, settings.Value.PaymentTolerance, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApiResponse<TableOccupancyRecoveryPreviewDto>.SuccessWithData(
            snapshot.ToPreview(), "Table occupancy recovery preview retrieved");
    }

    private async Task<SessionResolution> ResolveSessionAsync(
        Table table, Guid? requestedSessionId, CancellationToken cancellationToken)
    {
        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        var selectedSession = requestedSessionId is Guid sessionId
            ? await ResolveRequestedSessionAsync(table, sessionId, cancellationToken)
            : await ResolveUniqueOpenSessionAsync(table.Id, tableNumber, cancellationToken);
        if (selectedSession.Error is not null) return selectedSession;

        if (await HasOtherOpenSessionAsync(table.Id, tableNumber, selectedSession.Session, cancellationToken))
            return SessionResolution.Failed(Failure(
                "Another open visit is using this physical table; refresh the recovery preview.",
                ErrorCodes.TableServiceSessionAmbiguous));

        return selectedSession;
    }

    private async Task<SessionResolution> ResolveRequestedSessionAsync(
        Table table, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);
        if (session is null)
            return SessionResolution.Failed(Failure(
                "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound));
        if (!TableServiceSessionRowLock.IsPhysicalTableResolved(session, table))
            return SessionResolution.Failed(Failure(
                "The selected visit does not belong to this physical table.",
                ErrorCodes.TableServiceSessionAmbiguous));
        if (session.Status != TableServiceSessionStatus.Open)
            return SessionResolution.Failed(Failure(
                "A closed table visit cannot be recovered.", ErrorCodes.TableServiceSessionNotClosable));
        return SessionResolution.Resolved(session);
    }

    private async Task<SessionResolution> ResolveUniqueOpenSessionAsync(
        Guid tableId, int? tableNumber, CancellationToken cancellationToken)
    {
        var activeSessions = await FindOpenSessionsAsync(tableId, tableNumber, cancellationToken);
        if (activeSessions.Count > 1)
            return SessionResolution.Failed(Failure(
                "Multiple open visits point to this table; resolve the visit identity first.",
                ErrorCodes.TableServiceSessionAmbiguous));
        return SessionResolution.Resolved(activeSessions.SingleOrDefault());
    }

    private Task<bool> HasOtherOpenSessionAsync(
        Guid tableId, int? tableNumber, TableServiceSession? session, CancellationToken cancellationToken) =>
        context.TableServiceSessions.AsNoTracking().AnyAsync(value =>
            value.Id != (session == null ? Guid.Empty : session.Id)
            && value.Status == TableServiceSessionStatus.Open && value.ReleasedAt == null
            && (value.TableId == tableId
                || (!value.TableId.HasValue && tableNumber.HasValue && value.TableNumber == tableNumber)),
            cancellationToken);

    private Task<List<TableServiceSession>> FindOpenSessionsAsync(
        Guid tableId, int? tableNumber, CancellationToken cancellationToken) =>
        context.TableServiceSessions.AsNoTracking()
            .Where(value => value.Status == TableServiceSessionStatus.Open && value.ReleasedAt == null
                && (value.TableId == tableId
                    || (!value.TableId.HasValue && tableNumber.HasValue && value.TableNumber == tableNumber)))
            .OrderBy(value => value.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

    private static ApiResponse<TableOccupancyRecoveryPreviewDto> Failure(string message, string code) =>
        ApiResponse<TableOccupancyRecoveryPreviewDto>.FailureWithCode(message, code);

    private sealed record SessionResolution(
        TableServiceSession? Session,
        ApiResponse<TableOccupancyRecoveryPreviewDto>? Error)
    {
        public static SessionResolution Resolved(TableServiceSession? session) => new(session, null);
        public static SessionResolution Failed(ApiResponse<TableOccupancyRecoveryPreviewDto> error) => new(null, error);
    }
}
