using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Tells everyone that a settled payment confirmed an order — the kitchen over SSE, the diner by
/// email.
///
/// <para>
/// Split out of the settlement writer because the two fail differently and must not share a fate.
/// The writer's work is transactional and must land or roll back whole; this runs strictly AFTER the
/// commit, on money that is already booked, so a dead SMTP host or a dropped SSE client can never
/// surface as a failed payment.
/// </para>
/// </summary>
public interface ISettlementNotifier
{
    /// <summary>
    /// Broadcasts the status change and sends the order-confirmed email creation deferred.
    /// </summary>
    /// <param name="previousStatus">
    /// What the order was before settlement confirmed it. Passed rather than assumed: dine-in
    /// reaches <c>Confirmed</c> from <c>PendingApproval</c> as well as from <c>Pending</c>, and a
    /// hard-coded value would broadcast a transition that never happened.
    /// </param>
    Task NotifyConfirmedAsync(Order order, OrderStatus previousStatus, CancellationToken cancellationToken);
}
