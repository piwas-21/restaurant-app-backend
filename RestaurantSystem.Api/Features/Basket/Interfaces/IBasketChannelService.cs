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

    /// <summary>
    /// Clears the basket's channel, returning the basket afterwards (<c>null</c> when there was no
    /// basket to clear). Idempotent, and never destructive.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="SetOrderTypeAsync"/>, and deliberately NOT an overload of it
    /// (plan §9.17). Clearing has no two-phase protocol because it cannot conflict: a null channel
    /// is UNRESTRICTED, so every line the basket already holds stays orderable by definition. That
    /// asymmetry is the reason this is its own verb rather than a nullable order type on the PUT —
    /// with one endpoint, "unset" would have to travel through <c>RemoveConflicts</c> semantics that
    /// can never apply to it.
    /// <para>
    /// It does NOT create a basket. Unlike the set path — which upserts, because choosing a channel
    /// before adding anything is a real intent worth persisting — clearing a basket that does not
    /// exist has nothing to persist, and creating one would leave an orphan row under a session id
    /// the guest may never use again.
    /// </para>
    /// </remarks>
    Task<BasketDto?> ClearOrderTypeAsync(
        string sessionId,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
