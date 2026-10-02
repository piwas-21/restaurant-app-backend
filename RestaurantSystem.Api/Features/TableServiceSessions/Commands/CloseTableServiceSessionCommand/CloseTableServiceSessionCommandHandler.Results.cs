using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;

public sealed partial class CloseTableServiceSessionCommandHandler
{
    private async Task<ApiResponse<TableServiceSessionDto>> ReadResultAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _reader.ReadAsync(id, cancellationToken);
        return result is null
            ? NotFound()
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session closed");
    }

    private static ApiResponse<TableServiceSessionDto> NotFound() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<TableServiceSessionDto> Stale(int version) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            $"The service session is stale; current version is {version}.",
            ErrorCodes.TableServiceSessionStale);

    private static ApiResponse<TableServiceSessionDto> Ambiguous() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            TableBillTargetResolver.AmbiguousMessage, ErrorCodes.TableServiceSessionAmbiguous);
    private ApiResponse<TableServiceSessionDto> Unresolved(decimal outstanding, List<string> unresolved)
    {
        var errors = new List<string>();
        if (outstanding > _paymentTolerance)
            errors.Add($"The session still has an outstanding balance of {outstanding:0.00}.");
        if (unresolved.Count > 0)
            errors.Add("The session still has unresolved rounds: " + string.Join(", ", unresolved));
        return ApiResponse<TableServiceSessionDto>.FailureWithCode(
            errors, ErrorCodes.TableServiceSessionNotClosable);
    }

}
