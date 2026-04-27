/**
 * Date-range helpers for the cashier orders view.
 *
 * "Today" is anchored to the browser's local timezone — the cashier
 * thinks of "today" in their wall-clock, not in UTC. The returned
 * Date objects are absolute instants; serialise via `.toISOString()`
 * before sending to the backend (which compares verbatim in UTC; see
 * GetOrdersQuery XML doc).
 */

export function startOfTodayLocal(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

export function endOfTodayLocal(): Date {
  const d = new Date();
  d.setHours(24, 0, 0, 0);
  return d;
}
