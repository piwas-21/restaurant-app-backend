using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RepairLegacyTableServiceSessionCommand;

public sealed partial class RepairLegacyTableServiceSessionCommandHandler
{
    private static ApiResponse<TableServiceSessionDto> ToResponse(
        TableServiceSessionDto? result, int adoptedOrderCount)
    {
        if (result is null)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The repaired table service session could not be read back.",
                ErrorCodes.TableServiceSessionNotFound);
        }

        var successMessage = adoptedOrderCount == 0
            ? "Table service session already resolved"
            : "Legacy table orders adopted into the table service session";
        return ApiResponse<TableServiceSessionDto>.SuccessWithData(result, successMessage);
    }
}
