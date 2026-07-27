using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Owns the basket's order type (channel) and reconciliation of lines a new channel forbids.
/// </summary>
public interface IBasketChannelService
{
    /// <summary>
    /// Sets the basket's channel. Two-phase: with <paramref name="removeConflicts"/> false (the
    /// default the client should try first) a basket holding forbidden lines is left COMPLETELY
    /// unchanged and the conflicts are returned, so the guest can confirm before anything is lost.
    /// Repeat with true to remove those lines and apply the switch.
    /// </summary>
    Task<BasketChannelSwitchDto> SetOrderTypeAsync(
        string sessionId,
        Guid? userId,
        OrderType orderType,
        bool removeConflicts,
        CancellationToken cancellationToken = default);
}
