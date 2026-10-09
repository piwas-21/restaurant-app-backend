using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

internal static class TableServiceSessionOpenHelpers
{
    public static async Task<ApiResponse<TableServiceSessionDto>?> ValidateCanCreateAsync(
        ApplicationDbContext context,
        Table lockedTable,
        TableIdentity identity,
        decimal paymentTolerance,
        bool readinessEnabled,
        CancellationToken cancellationToken)
    {
        var legacyQuery = TableServiceSessionCloseRules.ForUnassignedSession(
            context.Orders.AsNoTracking(), context.Set<TableOccupancyRecoveryDisposition>(),
            identity.Id, identity.Number);
        var hasBlockingLegacyRound = await legacyQuery
            .Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .AnyAsync(TableServiceSessionCloseRules.BlockingLegacyQuery(paymentTolerance), cancellationToken);
        if (hasBlockingLegacyRound)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                TableBillTargetResolver.AmbiguousMessage,
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        if (!readinessEnabled) return null;

        if (await TableReadinessLegacyRules.HasOpenVisitAsync(
                context, identity.Id, identity.Number, cancellationToken)
            || await TableReadinessLegacyRules.HasBlockingLegacyRoundAsync(
                context, identity.Id, identity.Number, paymentTolerance, cancellationToken))
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "Resolve ambiguous legacy table activity before opening a new visit.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        return lockedTable.ReadinessState == TableReadinessState.ReadyForGuests
            ? null
            : ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "Mark the table ready for guests before opening a new visit.",
                ErrorCodes.TableReadinessNotAvailable);
    }

    public static Task<List<Guid>> FindOpenSessionIdsAsync(
        ApplicationDbContext context, TableIdentity table, CancellationToken cancellationToken) =>
        context.TableServiceSessions
            .AsNoTracking()
            .Where(session => (session.TableId == table.Id
                || (table.Number.HasValue && session.TableId == null
                    && session.TableNumber == table.Number))
                && session.Status == TableServiceSessionStatus.Open
                && session.ReleasedAt == null)
            .OrderBy(session => session.Id)
            .Select(session => session.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

    public static async Task<ApiResponse<TableServiceSessionDto>> ReadExistingSessionAsync(
        ITableServiceSessionReader reader, Guid sessionId, CancellationToken cancellationToken)
    {
        var authoritative = await reader.ReadAsync(sessionId, cancellationToken);
        return authoritative is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The existing table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(
                authoritative, "Table service session already open");
    }

    public static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && (postgres.ConstraintName?.Contains("table_number", StringComparison.OrdinalIgnoreCase) == true
            || postgres.ConstraintName?.Contains("table_id", StringComparison.OrdinalIgnoreCase) == true);
}
