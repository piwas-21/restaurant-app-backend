using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

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

    public OpenTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        ITableIdentityResolver tables,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _tables = tables;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        OpenTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.ResolveActiveAsync(
            command.TableId, command.TableNumber, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await FindOpenSessionAsync(table, cancellationToken);
        if (existing is not null)
        {
            var authoritative = await _reader.ReadAsync(existing.Id, cancellationToken);
            return authoritative is null
                ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                    "The existing table service session could not be read back.",
                    ErrorCodes.TableServiceSessionNotFound)
                : ApiResponse<TableServiceSessionDto>.SuccessWithData(
                    authoritative, "Table service session already open");
        }

        var tenantCurrency = await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        var currency = CurrencyCode.Normalize(command.Currency) ?? CurrencyCode.Normalize(tenantCurrency);
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableId = table.Id,
            TableNumber = table.Number,
            Currency = currency,
            Version = 1,
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier(),
        };

        _context.TableServiceSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenSessionConflict(ex))
        {
            _context.ChangeTracker.Clear();
            var authoritative = await FindOpenSessionAsync(table, cancellationToken);
            if (authoritative is not null)
            {
                var dto = await _reader.ReadAsync(authoritative.Id, cancellationToken);
                if (dto is not null)
                {
                    return ApiResponse<TableServiceSessionDto>.SuccessWithData(
                        dto, "Table service session already open");
                }
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

    private Task<TableServiceSession?> FindOpenSessionAsync(
        TableIdentity table, CancellationToken cancellationToken) =>
        _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => (session.TableId == table.Id
                || (table.Number.HasValue && session.TableId == null
                    && session.TableNumber == table.Number))
                && session.Status == Domain.Common.Enums.TableServiceSessionStatus.Open,
                cancellationToken);

    private static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && (postgres.ConstraintName?.Contains("table_number", StringComparison.OrdinalIgnoreCase) == true
            || postgres.ConstraintName?.Contains("table_id", StringComparison.OrdinalIgnoreCase) == true);
}
