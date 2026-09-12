using System.Text.Json.Serialization;
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
    [JsonRequired]
    public int TableNumber { get; init; }
    public string? Currency { get; init; }
}

public sealed class OpenTableServiceSessionCommandHandler
    : ICommandHandler<OpenTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableServiceSessionReader _reader;
    private readonly TimeProvider _timeProvider;

    public OpenTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        OpenTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _context.TableServiceSessions
            .AsNoTracking()
            .AnyAsync(session => session.TableNumber == command.TableNumber
                && session.Status == Domain.Common.Enums.TableServiceSessionStatus.Open,
                cancellationToken);
        if (existing)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                $"Table {command.TableNumber} already has an open service session.",
                ErrorCodes.TableServiceSessionAlreadyOpen);
        }

        var tenantCurrency = await _context.RestaurantInfo
            .AsNoTracking()
            .Select(info => info.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        var currency = CurrencyCode.Normalize(command.Currency) ?? CurrencyCode.Normalize(tenantCurrency);
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = command.TableNumber,
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
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                $"Table {command.TableNumber} already has an open service session.",
                ErrorCodes.TableServiceSessionAlreadyOpen);
        }

        var result = await _reader.ReadAsync(session.Id, cancellationToken);
        return result is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The new table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session opened");
    }

    private static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName?.Contains("table_number", StringComparison.OrdinalIgnoreCase) == true;
}
