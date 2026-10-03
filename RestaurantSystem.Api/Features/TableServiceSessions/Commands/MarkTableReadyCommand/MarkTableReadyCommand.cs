using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;

public sealed record MarkTableReadyCommand : ICommand<ApiResponse<TableReadinessOperationDto>>
{
    [JsonIgnore]
    public Guid TableId { get; set; }

    [JsonRequired]
    public Guid OperationId { get; set; }

    [JsonRequired]
    public int ExpectedReadinessVersion { get; set; }
}

public sealed record TableReadinessOperationDto(
    Guid TableId,
    Guid OperationId,
    string ReadinessState,
    int ReadinessVersion);

public sealed partial class MarkTableReadyCommandHandler
    : ICommandHandler<MarkTableReadyCommand, ApiResponse<TableReadinessOperationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantFeatures _features;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;

    public MarkTableReadyCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITenantFeatures features,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _currentUser = currentUser;
        _features = features;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
    }

    public async Task<ApiResponse<TableReadinessOperationDto>> Handle(
        MarkTableReadyCommand command, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue
            || _currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
        {
            return Failure("An authenticated table-service staff member is required.",
                ErrorCodes.TableReadinessStaffRequired);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var table = await TableServiceSessionRowLock.LoadTableAsync(
            _context, command.TableId, cancellationToken);
        if (table is null)
        {
            return Failure("The selected table was not found.", ErrorCodes.TableServiceTableNotFound);
        }

        var replayResponse = await ReplayIfPresentAsync(table, command, transaction, cancellationToken);
        if (replayResponse is not null) return replayResponse;

        if (!_features.TableVisitReadinessV1)
        {
            return Failure(
                "Explicit table readiness is not enabled for this tenant.",
                ErrorCodes.TableReadinessFeatureDisabled);
        }

        var refusal = await RecordPreconditionFailureAsync(table, command, transaction, cancellationToken);
        if (refusal is not null) return refusal;

        return await RecordSuccessAsync(table, command, transaction, cancellationToken);
    }
}
