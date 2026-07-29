using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.ClearBasketOrderTypeCommand;

/// <summary>
/// Clears the basket's order type, so the server stops judging adds against a channel the guest no
/// longer holds.
/// </summary>
/// <remarks>
/// Plan §9.17. The client already clears its own channel on two paths — the 24h TTL and
/// <c>useOrderTypeEnabledGuard</c> finding the chosen channel disabled — but had no way to tell the
/// server, because <c>PUT /api/Basket/order-type</c> takes a non-nullable, <c>[JsonRequired]</c>
/// order type. The basket stayed armed on the abandoned channel and every later add was judged
/// against it, so a guest holding no channel could still be refused for one.
/// <para>
/// Carries no body at all: the session header names the basket and there is nothing else to say.
/// </para>
/// </remarks>
public record ClearBasketOrderTypeCommand : ICommand<ApiResponse<BasketDto?>>
{
    // Set by the controller from the X-Session-Id header. [JsonIgnore] keeps it out of the request
    // schema so a body value cannot bind it (mirrors SetBasketOrderTypeCommand).
    [JsonIgnore]
    public string SessionId { get; set; } = string.Empty;
}

public class ClearBasketOrderTypeCommandHandler
    : ICommandHandler<ClearBasketOrderTypeCommand, ApiResponse<BasketDto?>>
{
    private readonly IBasketChannelService _channelService;
    private readonly ICurrentUserService _currentUserService;

    public ClearBasketOrderTypeCommandHandler(
        IBasketChannelService channelService,
        ICurrentUserService currentUserService)
    {
        _channelService = channelService;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<BasketDto?>> Handle(
        ClearBasketOrderTypeCommand command,
        CancellationToken cancellationToken)
    {
        // No try/catch — domain exceptions must reach the exception middleware so the caller gets a
        // real status code rather than a 200 carrying success:false. Same rule as the set handler.
        var basket = await _channelService.ClearOrderTypeAsync(
            command.SessionId,
            _currentUserService.UserId,
            cancellationToken);

        // "No basket" is a success: the guest asked for no channel and that is now true. The message
        // distinguishes the two so a client can log the difference without having to infer it from a
        // null payload.
        var message = basket is null
            ? "No basket to clear"
            : "Order type cleared";

        return ApiResponse<BasketDto?>.SuccessWithData(basket, message);
    }
}
