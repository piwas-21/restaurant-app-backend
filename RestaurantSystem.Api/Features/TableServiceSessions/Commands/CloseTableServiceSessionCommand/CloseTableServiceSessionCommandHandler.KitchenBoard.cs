using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;

public sealed partial class CloseTableServiceSessionCommandHandler
{
    private async Task<ApiResponse<TableServiceSessionDto>?> CheckKitchenBoardCloseAsync(
        TableServiceSession session, CancellationToken cancellationToken)
    {
        if (_features?.TableAccountV1 != true && _features?.OrderAmendmentsV1 != true)
            return null;

        var unresolved = await KitchenBoardCloseGuard.HasUnresolvedCorrectionAsync(
            _context, session.Id, session.TableId, session.TableNumber, cancellationToken);
        return unresolved
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "A kitchen correction still needs acknowledgement before closing this visit.",
                ErrorCodes.KitchenCorrectionUnresolved)
            : null;
    }
}
