using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.ReleaseTableServiceSessionCommand;

public sealed record ReleaseTableServiceSessionCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    [JsonIgnore]
    public Guid ServiceSessionId { get; set; }

    [JsonRequired]
    public int ExpectedVersion { get; init; }
}

public sealed class ReleaseTableServiceSessionCommandHandler
    : ICommandHandler<ReleaseTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;
    private readonly ITableGuestVisitRevoker _guestVisits;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    public ReleaseTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ITableServiceSessionReader reader,
        ITableGuestVisitRevoker guestVisits,
        ICurrentUserService currentUser,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _reader = reader;
        _guestVisits = guestVisits;
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        ReleaseTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var locked = await TableServiceSessionRowLock.LoadForLifecycleAsync(
            _context, command.ServiceSessionId, cancellationToken);
        var session = locked.Session;
        if (session is null) return NotFound();
        if (locked.IdentityChanged || session.TableId.HasValue && locked.Table?.Id != session.TableId.Value)
            return Stale(session.Version);

        if (session.Status != TableServiceSessionStatus.Open)
            return Refused("A closed table visit cannot release its table.");

        // A retry after a committed release returns the still-payable visit without requiring the
        // caller to keep the old version. It never applies the release twice.
        if (session.ReleasedAt.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return await ReadResultAsync(session.Id, cancellationToken);
        }

        if (session.Version != command.ExpectedVersion) return Stale(session.Version);
        if (await TableServicePaymentHandoffRules.HasPendingAsync(
                _context, session.Id, cancellationToken))
            return Refused("Resolve the pending cashier collection request before releasing this table.");
        var currentVisit = await _reader.ReadAsync(session.Id, cancellationToken);
        if (currentVisit?.HasUnassignedActiveOrders == true)
            return Refused("Resolve the unassigned legacy orders before releasing this table.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _guestVisits.RevokeForSessionAsync(session.Id, now, cancellationToken);
        if (locked.Table is not null)
        {
            locked.Table.ReadinessState = TableReadinessState.NeedsReset;
            locked.Table.ReadinessVersion++;
        }

        session.ReleasedAt = now;
        session.ReleasedBy = _currentUser.GetAuditIdentifier();
        session.Version++;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadResultAsync(session.Id, cancellationToken);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> ReadResultAsync(
        Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var result = await _reader.ReadAsync(serviceSessionId, cancellationToken);
        return result is null
            ? NotFound()
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table released; visit remains payable");
    }

    private static ApiResponse<TableServiceSessionDto> NotFound() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<TableServiceSessionDto> Stale(int version) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            $"The service session is stale; current version is {version}.", ErrorCodes.TableServiceSessionStale);

    private static ApiResponse<TableServiceSessionDto> Refused(string message) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(message, ErrorCodes.TableServiceSessionNotClosable);
}
