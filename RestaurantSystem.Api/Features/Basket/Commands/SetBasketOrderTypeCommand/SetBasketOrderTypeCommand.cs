using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Commands.SetBasketOrderTypeCommand;

/// <summary>
/// Sets the basket's order type, reporting (or removing) lines the new channel forbids.
/// </summary>
public record SetBasketOrderTypeCommand : ICommand<ApiResponse<BasketChannelSwitchDto>>
{
    // Set by the controller from the X-Session-Id header. [JsonIgnore] keeps it out of the request
    // schema so a body value cannot bind it (mirrors CreateOrderFromBasketCommand).
    [JsonIgnore]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>The channel to switch to. Required so an omitted value cannot silently default.</summary>
    [JsonRequired]
    public OrderType OrderType { get; set; }

    /// <summary>
    /// Opt in to removing forbidden lines. Leave false on the first call to get the conflict list
    /// without changing anything, then repeat with true once the guest confirms.
    /// </summary>
    public bool RemoveConflicts { get; set; }
}

public class SetBasketOrderTypeCommandHandler
    : ICommandHandler<SetBasketOrderTypeCommand, ApiResponse<BasketChannelSwitchDto>>
{
    private readonly IBasketChannelService _channelService;
    private readonly ICurrentUserService _currentUserService;

    public SetBasketOrderTypeCommandHandler(
        IBasketChannelService channelService,
        ICurrentUserService currentUserService)
    {
        _channelService = channelService;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<BasketChannelSwitchDto>> Handle(
        SetBasketOrderTypeCommand command,
        CancellationToken cancellationToken)
    {
        // No try/catch: domain exceptions (NotFoundException, BadRequestException) must reach the
        // exception middleware so the caller gets the real status code and message. Swallowing them
        // into a 200 + success:false is the bug this feature had to fix on the add-to-basket path.
        var result = await _channelService.SetOrderTypeAsync(
            command.SessionId,
            _currentUserService.UserId,
            command.OrderType,
            command.RemoveConflicts,
            cancellationToken);

        var message = result.Applied
            ? "Order type updated"
            : "Some items are not available for this order type";

        return ApiResponse<BasketChannelSwitchDto>.SuccessWithData(result, message);
    }
}
