using System.Diagnostics.CodeAnalysis;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Interfaces;

/// <summary>
/// Builds the <see cref="Order"/> row a <c>CreateOrderCommand</c> asks for — order number, quick-
/// action token, the guest's language, the channel-override audit trail and the delivery-address
/// snapshot — up to but not including <c>DbContext.Add</c>. Items, pricing, fidelity and payments
/// stay in the handler: they need the order to exist first.
/// </summary>
/// <remarks>
/// Extracted from <c>CreateOrderCommandHandler</c> in GAP-2 S4. The handler was at its 200-LOC
/// limit (CLAUDE.md §4) with thirteen constructor dependencies, and the language capture this
/// slice adds belongs with the rest of the row's creation-time facts rather than as a fourteenth.
/// </remarks>
public interface IOrderFactory
{
    /// <summary>
    /// The order and the creation-time values the handler still needs, or an <c>Error</c> when the
    /// request cannot produce an order (a delivery with no resolvable address). Nothing is added to
    /// the change tracker here.
    /// </summary>
    /// <param name="userId">
    /// The account the order belongs to, resolved by the handler. Passed in rather than recomputed
    /// here so the row and the language captured for it can never be keyed off different ids.
    /// </param>
    /// <param name="language">
    /// The guest's language, already resolved (S4). Passed in rather than looked up here because
    /// this method runs inside the handler's transaction and behind the order-number generator's
    /// advisory lock, where an extra round-trip is both a serialisation cost and a new way for an
    /// order to fail over a field no money depends on.
    /// </param>
    Task<OrderDraft> CreateAsync(
        CreateOrderCommand command, Guid? userId, string language, CancellationToken cancellationToken);
}

/// <summary>
/// A built-but-unsaved order plus the creation-time values the handler reuses. <see cref="Order"/>
/// is null exactly when <see cref="Error"/> is not.
/// </summary>
public sealed record OrderDraft(
    Order? Order,
    string? Error,
    Guid? UserId,
    string AuditId,
    DateTime Now,
    bool PaysOnline)
{
    /// <summary>
    /// True when no order was built. The attributes are what let the handler drop the
    /// null-forgiving operator: a future return that sets neither field would otherwise surface as
    /// a NullReferenceException inside an open transaction, or as a status-history row stamped
    /// <c>0001-01-01</c> by an empty audit id.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    [MemberNotNullWhen(false, nameof(Order))]
    public bool IsFailed => Error is not null;

    public static OrderDraft Failed(string error) =>
        new(null, error, null, string.Empty, default, false);
}
