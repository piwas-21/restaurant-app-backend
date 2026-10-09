using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

internal static class TableReadinessOperationResponses
{
    public static ApiResponse<TableReadinessOperationDto> ToResponse(TableReadyOperation operation) =>
        operation.Succeeded
            ? ApiResponse<TableReadinessOperationDto>.SuccessWithData(new TableReadinessOperationDto(
                operation.TableId,
                operation.OperationId,
                operation.OutcomeState.ToString(),
                operation.OutcomeReadinessVersion))
            : FailureForOutcome(operation.OutcomeErrorCode);

    private static ApiResponse<TableReadinessOperationDto> FailureForOutcome(string? errorCode) =>
        errorCode switch
        {
            ErrorCodes.TableServiceTableInactive => Failure(
                "The selected table is inactive.", ErrorCodes.TableServiceTableInactive),
            ErrorCodes.TableReadinessVersionStale => Failure(
                "The table readiness changed. Refresh the floor before trying again.",
                ErrorCodes.TableReadinessVersionStale),
            ErrorCodes.TableReadinessVisitOpen => Failure(
                "Close the current table visit before marking the table ready.",
                ErrorCodes.TableReadinessVisitOpen),
            ErrorCodes.TableServiceSessionAmbiguous => Failure(
                "Resolve ambiguous legacy table activity before marking the table ready.",
                ErrorCodes.TableServiceSessionAmbiguous),
            _ => Failure(
                "The table does not currently need a readiness reset.",
                ErrorCodes.TableReadinessNotAvailable)
        };

    private static ApiResponse<TableReadinessOperationDto> Failure(string message, string code) =>
        ApiResponse<TableReadinessOperationDto>.FailureWithCode(message, code);
}
