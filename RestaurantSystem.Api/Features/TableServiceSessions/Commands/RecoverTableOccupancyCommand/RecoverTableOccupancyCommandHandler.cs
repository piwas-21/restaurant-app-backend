using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed partial class RecoverTableOccupancyCommandHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITableGuestVisitRevoker guestVisits,
    ITenantFeatures features,
    TimeProvider timeProvider,
    IOptions<TableServiceSessionSettings> settings)
    : ICommandHandler<RecoverTableOccupancyCommand, ApiResponse<TableOccupancyRecoveryOperationDto>>
{
    public async Task<ApiResponse<TableOccupancyRecoveryOperationDto>> Handle(
        RecoverTableOccupancyCommand command, CancellationToken cancellationToken)
    {
        if (!features.TableVisitReadinessV1)
            return Failure("Explicit table occupancy recovery is not enabled for this tenant.",
                ErrorCodes.TableReadinessFeatureDisabled);
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue
            || currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
        {
            return Failure("An authenticated table-service staff member is required for table occupancy recovery.",
                ErrorCodes.TableReadinessStaffRequired);
        }
        if (!command.ConfirmRecovery || string.IsNullOrWhiteSpace(command.Reason))
            return Failure("Confirm the recovery and provide an audit reason.",
                ErrorCodes.TableServiceSessionNotClosable);

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var table = await TableServiceSessionRowLock.LoadTableAsync(
                context, command.TableId, cancellationToken);
            if (table is null)
                return Failure("The selected table was not found.", ErrorCodes.TableServiceTableNotFound);

            var requestHash = HashRequest(command);
            var replay = await ReplayIfPresentAsync(table.Id, command, requestHash, transaction, cancellationToken);
            if (replay is not null) return replay;

            var result = await RecoverLockedAsync(table, command, requestHash, cancellationToken);
            if (result.Error is not null) return result.Error;
            if (result.Operation is null)
                return Failure("The recovery outcome could not be constructed.",
                    ErrorCodes.TableOccupancyRecoveryOperationMismatch);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ApiResponse<TableOccupancyRecoveryOperationDto>.SuccessWithData(
                result.Operation, "Table occupancy recovered; prior orders and financial history were preserved.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure("The table changed during recovery. Refresh the preview and confirm again.",
                ErrorCodes.TableOccupancyRecoveryPreviewStale);
        }
        catch (Exception exception) when (PostgresConcurrencyAborts.IsMatch(exception, out _))
        {
            return Failure("The table changed during recovery. Refresh the preview and confirm again.",
                ErrorCodes.TableOccupancyRecoveryPreviewStale);
        }
    }

    private static ApiResponse<TableOccupancyRecoveryOperationDto> Failure(string message, string code) =>
        ApiResponse<TableOccupancyRecoveryOperationDto>.FailureWithCode(message, code);

    private static string HashRequest(RecoverTableOccupancyCommand command) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("|",
                command.TableId,
                command.OperationId,
                command.ServiceSessionId,
                command.ExpectedReadinessVersion,
                command.ExpectedSessionVersion,
                command.ExpectedAccountRevision,
                command.PreviewFingerprint.ToUpperInvariant(),
                command.ConfirmRecovery,
                command.Reason.Trim()))));
}
