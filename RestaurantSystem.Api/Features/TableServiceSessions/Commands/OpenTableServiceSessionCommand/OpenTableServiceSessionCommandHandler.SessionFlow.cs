using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;

public sealed partial class OpenTableServiceSessionCommandHandler
{
    private async Task<ApiResponse<TableServiceSessionDto>?> ResolveExistingSessionAsync(
        TableIdentity identity,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existingIds = await TableServiceSessionOpenHelpers.FindOpenSessionIdsAsync(
            _context, identity, cancellationToken);
        if (existingIds.Count > 1) return AmbiguousSessionResponse();
        if (existingIds.Count == 0) return null;

        var existing = await TableServiceSessionRowLock.LoadAsync(
            _context, existingIds[0], cancellationToken);
        if (existing?.Status != TableServiceSessionStatus.Open || existing.ReleasedAt.HasValue) return null;
        if (existing.TableId != identity.Id && ReadinessEnabled()) return UnstableVisitResponse();

        await transaction.CommitAsync(cancellationToken);
        return await TableServiceSessionOpenHelpers.ReadExistingSessionAsync(
            _reader, existing.Id, cancellationToken);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> CreateSessionAsync(
        OpenTableServiceSessionCommand command,
        TableIdentity identity,
        DateTime now,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tenantCurrency = await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableId = identity.Id,
            TableNumber = identity.Number,
            Currency = CurrencyCode.Normalize(command.Currency) ?? CurrencyCode.Normalize(tenantCurrency),
            Version = 1,
            BillingAllocationVersion = 1,
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        };

        _context.TableServiceSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TableServiceSessionOpenHelpers.IsOpenSessionConflict(exception))
        {
            return await RecoverOpenSessionConflictAsync(identity, transaction, cancellationToken);
        }

        var result = await _reader.ReadAsync(session.Id, cancellationToken);
        return result is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The new table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session opened");
    }

    private async Task<ApiResponse<TableServiceSessionDto>> RecoverOpenSessionConflictAsync(
        TableIdentity identity,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        await _context.Database.UseTransactionAsync(null, cancellationToken);
        _context.ChangeTracker.Clear();
        var authoritativeIds = await TableServiceSessionOpenHelpers.FindOpenSessionIdsAsync(
            _context, identity, cancellationToken);
        if (authoritativeIds.Count > 1) return AmbiguousSessionResponse();
        if (authoritativeIds.Count == 0)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The table service session could not be opened because the table changed concurrently.",
                ErrorCodes.TableServiceSessionAlreadyOpen);
        }

        if (ReadinessEnabled())
        {
            var existingTableId = await _context.TableServiceSessions.AsNoTracking()
                .Where(session => session.Id == authoritativeIds[0])
                .Select(session => session.TableId)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingTableId != identity.Id) return UnstableVisitResponse();
        }

        return await TableServiceSessionOpenHelpers.ReadExistingSessionAsync(
            _reader, authoritativeIds[0], cancellationToken);
    }

    private bool ReadinessEnabled() => _features?.TableVisitReadinessV1 == true;

    private static ApiResponse<TableServiceSessionDto> AmbiguousSessionResponse() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "More than one open visit refers to this table. Resolve the legacy identity before continuing.",
            ErrorCodes.TableServiceSessionAmbiguous);

    private static ApiResponse<TableServiceSessionDto> UnstableVisitResponse() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "This legacy visit has no stable table identity. Repair it explicitly before using the table.",
            ErrorCodes.TableServiceSessionAmbiguous);
}
