using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;

public sealed partial class ReleaseStaffCounterOrderCommandHandler
{
    private static bool IsConcurrencyAbort(Exception exception) => exception is DbUpdateConcurrencyException
        || PostgresConcurrencyAborts.IsMatch(exception, out _);

    private async Task<ApiResponse<OrderDto>> ReplayAsync(
        StaffOrderOperationReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Outcome == StaffOrderOperationReplayOutcome.PayloadMismatch)
            return ApiResponse<OrderDto>.FailureWithCode(
                "This release operation was already submitted with different details.",
                ErrorCodes.StaffOrderOperationPayloadMismatch);
        if (replay.Outcome == StaffOrderOperationReplayOutcome.OperationIdReused)
            return ApiResponse<OrderDto>.FailureWithCode(
                "This operation id is already used by another staff operation.",
                ErrorCodes.StaffOrderOperationIdReused);
        if (replay.Order == null)
            return ApiResponse<OrderDto>.Failure("The released order could not be found.");

        var dto = await _responses.ProjectAsync(replay.Order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Order release already recorded");
    }

    private static bool IsOperationKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;
}
