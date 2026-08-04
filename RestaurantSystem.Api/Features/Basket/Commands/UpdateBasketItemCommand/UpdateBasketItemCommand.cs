using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;

public record UpdateBasketItemCommand(
    string SessionId,
    Guid BasketItemId,
    int Quantity,
    string? SpecialInstructions
) : ICommand<ApiResponse<BasketDto>>;

public class UpdateBasketItemCommandHandler : ICommandHandler<UpdateBasketItemCommand, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<UpdateBasketItemCommandHandler> _logger;

    public UpdateBasketItemCommandHandler(
        IBasketService basketService,
        ILogger<UpdateBasketItemCommandHandler> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(UpdateBasketItemCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var updateDto = new UpdateBasketItemDto
            {
                Quantity = command.Quantity,
                SpecialInstructions = command.SpecialInstructions
            };

            var basket = await _basketService.UpdateBasketItemAsync(
                command.SessionId,
                command.BasketItemId,
                updateDto);

            _logger.LogInformation("Updated basket item {BasketItemId} for session {SessionId}",
                command.BasketItemId, command.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket, "Basket item updated successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update basket item");
            return ApiResponse<BasketDto>.Failure(ex.Message);
        }

        // NOTE: there is deliberately no catch-all handler for Exception below this point — the same
        // removal `AddToBasketCommandHandler` already carries, and for the same reason. The one that
        // used to sit here turned EVERY failure into HTTP 200 plus success:false and replaced the
        // message with "An error occurred while updating basket item", so the two 404s this endpoint
        // can raise — "Basket not found" (the row is gone: reaped by BasketCleanupService, or an
        // expired session) and "Basket item not found" (the guest removed it in another tab) —
        // arrived at the client as one indistinguishable generic string. The client could not tell
        // them apart, so it could not resync on the benign one or report the real one; its
        // already-gone recovery was dead code, because `getErrorMessage` returns null for a 200.
        // Both now reach the exception middleware and carry ErrorCodes.BasketNotFound /
        // BasketItemNotFound (frontend issue #415), and an unexpected failure surfaces as a 500
        // instead of masquerading as a handled one.
        //
        // The `InvalidOperationException` catch above is NOT part of that and still answers 200 +
        // success:false with `ex.Message`: EF raises it for tracking conflicts and "Sequence
        // contains no elements", and ObjectDisposedException derives from it. So the 200 shape
        // survives for those, and the client still reads them through `!response.data` rather than
        // as an ApiError — the same residual case `basketService.ts` documents on the add path.
    }
}
