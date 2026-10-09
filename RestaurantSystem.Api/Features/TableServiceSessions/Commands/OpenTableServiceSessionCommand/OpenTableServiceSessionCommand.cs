using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;

public record OpenTableServiceSessionCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    public Guid? TableId { get; init; }
    public int? TableNumber { get; init; }
    public string? Currency { get; init; }
}

public sealed partial class OpenTableServiceSessionCommandHandler
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

        var existingResponse = await ResolveExistingSessionAsync(
            revalidated, transaction, cancellationToken);
        if (existingResponse is not null) return existingResponse;

        var readinessFailure = await TableServiceSessionOpenHelpers.ValidateCanCreateAsync(
            _context, lockedTable, revalidated, _paymentTolerance,
            _features?.TableVisitReadinessV1 == true, cancellationToken);
        if (readinessFailure is not null) return readinessFailure;

        return await CreateSessionAsync(command, revalidated, now, transaction, cancellationToken);
    }
}
