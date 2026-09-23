using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RepairLegacyTableServiceSessionCommand;

public record RepairLegacyTableServiceSessionCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    public Guid? TableId { get; init; }
    public int? TableNumber { get; init; }
    public string? Currency { get; init; }
}

public sealed class RepairLegacyTableServiceSessionCommandHandler
    : ICommandHandler<RepairLegacyTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableServiceSessionReader _reader;
    private readonly ITableIdentityResolver _tables;
    private readonly decimal _paymentTolerance;
    private readonly TimeProvider _timeProvider;

    public RepairLegacyTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        ITableIdentityResolver tables,
        IOptions<TableServiceSessionSettings>? settings = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _tables = tables;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        RepairLegacyTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.ResolveActiveAsync(command.TableId, command.TableNumber, cancellationToken);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await RepairAsync(table, command.Currency, cancellationToken);
            }
            catch (DbUpdateException exception) when (IsOpenSessionConflict(exception) && attempt == 0)
            {
                _context.ChangeTracker.Clear();
            }
        }

        return ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "The table service session changed concurrently; refresh and try again.",
            ErrorCodes.TableServiceSessionAlreadyOpen);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> RepairAsync(
        TableIdentity table, string? requestedCurrency, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var lockedTable = await TableServiceSessionRowLock.LoadTableAsync(
            _context, table.Id, cancellationToken);
        if (lockedTable is null || !lockedTable.IsActive)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The selected table is no longer active.", ErrorCodes.TableServiceTableInactive);
        }

        table = ToIdentity(lockedTable);
        var session = await FindOpenSessionAsync(table, cancellationToken);
        var created = session is null;
        if (session is not null)
        {
            session = await TableServiceSessionRowLock.LoadAsync(
                _context, session.Id, cancellationToken);
            if (session is null)
            {
                created = true;
            }
        }
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (session is null)
        {
            var tenantCurrency = await _context.RestaurantInfo
                .AsNoTracking()
                .Select(info => info.Currency)
                .FirstOrDefaultAsync(cancellationToken);
            session = new TableServiceSession
            {
                Id = Guid.NewGuid(),
                TableId = table.Id,
                TableNumber = table.Number,
                Currency = CurrencyCode.Normalize(requestedCurrency) ?? CurrencyCode.Normalize(tenantCurrency),
                Version = 1,
                OpenedAt = now,
                CreatedAt = now,
                CreatedBy = _currentUser.GetAuditIdentifier(),
            };
            _context.TableServiceSessions.Add(session);
        }
        else
        {
            var identityChanged = session.TableId != table.Id || session.TableNumber != table.Number;
            session.TableId = table.Id;
            session.TableNumber = table.Number;
            if (identityChanged)
            {
                session.Version++;
            }
        }
        var legacyOrders = await FindBlockingLegacyOrdersAsync(table, cancellationToken);
        foreach (var order in legacyOrders)
        {
            order.TableId = table.Id;
            order.TableNumber = table.Number;
            order.TableLabel = table.Label;
            order.ServiceSessionId = session.Id;
        }

        if (!created && legacyOrders.Count > 0)
        {
            session.Version++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await _reader.ReadAsync(session.Id, cancellationToken);
        return ToResponse(result, legacyOrders.Count);
    }

    private static ApiResponse<TableServiceSessionDto> ToResponse(TableServiceSessionDto? result, int adoptedOrderCount)
    {
        if (result is null)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The repaired table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound);
        }

        return ApiResponse<TableServiceSessionDto>.SuccessWithData(result, adoptedOrderCount == 0
            ? "Table service session already resolved"
            : "Legacy table orders adopted into the table service session");
    }

    private Task<List<Order>> FindBlockingLegacyOrdersAsync(
        TableIdentity table, CancellationToken cancellationToken)
    {
        var query = TableServiceSessionCloseRules.ForUnassignedSession(
            _context.Orders, table.Id, table.Number);
        return query
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .Where(order => order.Status != OrderStatus.Completed
                && order.Status != OrderStatus.Cancelled
                || order.Status == OrderStatus.Completed
                && order.RemainingAmount > _paymentTolerance)
            .ToListAsync(cancellationToken);
    }

    private Task<TableServiceSession?> FindOpenSessionAsync(
        TableIdentity table, CancellationToken cancellationToken) =>
        _context.TableServiceSessions
            .SingleOrDefaultAsync(session => (session.TableId == table.Id
                || (table.Number.HasValue && session.TableId == null
                    && session.TableNumber == table.Number))
                && session.Status == TableServiceSessionStatus.Open,
                cancellationToken);

    private static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && (postgres.ConstraintName?.Contains("table_number", StringComparison.OrdinalIgnoreCase) == true
            || postgres.ConstraintName?.Contains("table_id", StringComparison.OrdinalIgnoreCase) == true);

    private static TableIdentity ToIdentity(Table table)
    {
        var isCanonicalNumber = int.TryParse(
            table.TableNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0
            && number.ToString(CultureInfo.InvariantCulture) == table.TableNumber;
        return new TableIdentity(table.Id, table.TableNumber, isCanonicalNumber ? number : null);
    }
}
