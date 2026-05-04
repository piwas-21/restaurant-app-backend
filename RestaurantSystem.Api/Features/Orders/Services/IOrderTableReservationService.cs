using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Reserves a restaurant table when a Dine-in order is created. Best-effort:
/// any failure (table not found, table already reserved, DB error) is
/// logged but never thrown — order creation must not fail because the
/// table couldn't be reserved.
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.11.
/// </summary>
public interface IOrderTableReservationService
{
    /// <summary>
    /// If the order is a Dine-in order with a <c>TableNumber</c>, look up
    /// the matching <c>Table</c> and create a 2-hour <c>TableReservation</c>
    /// — but only if the table doesn't already have an active reservation
    /// that hasn't expired. Persists the reservation via
    /// <c>SaveChangesAsync</c>; the caller does not need to.
    /// </summary>
    Task ReserveForDineInAsync(Order order, CancellationToken cancellationToken);
}
