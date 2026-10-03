using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;

public record OpenTableServiceSessionCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    public Guid? TableId { get; init; }
    public int? TableNumber { get; init; }
    public string? Currency { get; init; }
}

public sealed class OpenTableServiceSessionCommandHandler
    : ICommandHandler<OpenTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableServiceSessionReader _reader;
    private readonly ITableIdentityResolver _tables;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;
    private readonly ITenantFeatures? _features;

    public OpenTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        ITableIdentityResolver tables,
        IOptions<TableServiceSessionSettings>? settings = null,
        TimeProvider? timeProvider = null,
        ITenantFeatures? features = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _tables = tables;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _features = features;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        OpenTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.ResolveActiveAsync(
            command.TableId, command.TableNumber, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var lockedTable = await TableServiceSessionRowLock.LoadTableAsync(
            _context, table.Id, cancellationToken);
        if (lockedTable is null || !lockedTable.IsActive)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The selected table is no longer active.", ErrorCodes.TableServiceTableInactive);
        }

        var revalidated = await _tables.ResolveActiveAsync(
            command.TableId, command.TableNumber, cancellationToken);
        if (revalidated.Id != lockedTable.Id)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The selected table identity changed; refresh the floor before opening a visit.",
                ErrorCodes.TableServiceTableMismatch);
        }

        var existingIds = await TableServiceSessionOpenHelpers.FindOpenSessionIdsAsync(
            _context, revalidated, cancellationToken);
        if (existingIds.Count > 1)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "More than one open visit refers to this table. Resolve the legacy identity before continuing.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        if (existingIds.Count == 1)
        {
            var existing = await TableServiceSessionRowLock.LoadAsync(
                _context, existingIds[0], cancellationToken);
            if (existing?.Status == Domain.Common.Enums.TableServiceSessionStatus.Open)
            {
                if (existing.TableId != revalidated.Id
                    && _features?.TableVisitReadinessV1 == true)
                {
                    return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                        "This legacy visit has no stable table identity. Repair it explicitly before using the table.",
                        ErrorCodes.TableServiceSessionAmbiguous);
                }

                await transaction.CommitAsync(cancellationToken);
                return await TableServiceSessionOpenHelpers.ReadExistingSessionAsync(
                    _reader, existing.Id, cancellationToken);
            }
        }

        var readinessFailure = await TableServiceSessionOpenHelpers.ValidateCanCreateAsync(
            _context, lockedTable, revalidated, _paymentTolerance,
            _features?.TableVisitReadinessV1 == true, cancellationToken);
        if (readinessFailure is not null) return readinessFailure;

        var tenantCurrency = await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        var currency = CurrencyCode.Normalize(command.Currency) ?? CurrencyCode.Normalize(tenantCurrency);
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableId = revalidated.Id,
            TableNumber = revalidated.Number,
            Currency = currency,
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
        catch (DbUpdateException ex) when (TableServiceSessionOpenHelpers.IsOpenSessionConflict(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            await _context.Database.UseTransactionAsync(null, cancellationToken);
            _context.ChangeTracker.Clear();
            var authoritativeIds = await TableServiceSessionOpenHelpers.FindOpenSessionIdsAsync(
                _context, revalidated, cancellationToken);
            if (authoritativeIds.Count > 1)
            {
                return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                    "More than one open visit refers to this table. Resolve the legacy identity before continuing.",
                    ErrorCodes.TableServiceSessionAmbiguous);
            }

            if (authoritativeIds.Count == 1)
            {
                if (_features?.TableVisitReadinessV1 == true
                    && await _context.TableServiceSessions.AsNoTracking()
                        .Where(session => session.Id == authoritativeIds[0])
                        .Select(session => session.TableId)
                        .SingleOrDefaultAsync(cancellationToken) != revalidated.Id)
                {
                    return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                        "This legacy visit has no stable table identity. Repair it explicitly before using the table.",
                        ErrorCodes.TableServiceSessionAmbiguous);
                }

                return await TableServiceSessionOpenHelpers.ReadExistingSessionAsync(
                    _reader, authoritativeIds[0], cancellationToken);
            }

            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The table service session could not be opened because the table changed concurrently.",
                ErrorCodes.TableServiceSessionAlreadyOpen);
        }

        var result = await _reader.ReadAsync(session.Id, cancellationToken);
        return result is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The new table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session opened");
    }

}
