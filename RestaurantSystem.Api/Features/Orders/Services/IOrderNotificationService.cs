using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Order-related notifications: email + SSE. Wraps <c>IEmailService</c>
/// and <c>IOrderEventService</c> so the per-order composition logic
/// (email items, delivery address, fallback names) and the boilerplate
/// best-effort try/catch + logging live in one place.
///
/// Extracted in Sprint 2 task 2.10 (email surface) and task 2.11 slice 4
/// (SSE surface). Closes
/// <see href="https://gitlab.com/restaurant-app3282120/backend/-/issues/13">#13</see>:
/// the admin fire-and-forget path resolves a fresh DI scope so it
/// outlives the request-scope's <c>IEmailService</c>.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Sends the "order confirmed" email for an auto-confirmed order
    /// (e.g. Dine-in). Failures are logged and swallowed — order
    /// creation must not fail because email delivery did.
    /// </summary>
    Task SendOrderConfirmedAsync(Order order, int estimatedPreparationMinutes, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the "order received" email to the customer and the admin
    /// notification email (the latter fire-and-forget against a fresh
    /// DI scope). The customer email is awaited; failures bubble to the
    /// caller. The admin email is fire-and-forget; its failures are
    /// logged inside the lambda.
    /// </summary>
    Task SendOrderConfirmationAsync(OrderDto order);

    /// <summary>
    /// Best-effort SSE broadcast of an order-created event. Wraps
    /// <c>IOrderEventService.NotifyOrderCreated</c> with try/catch +
    /// attempt/success/failure logging — failures never propagate.
    /// </summary>
    Task NotifyOrderCreatedAsync(OrderDto order);

    /// <summary>
    /// Best-effort SSE broadcast of a focus-order-updated event. No-op
    /// if the order is not a focus order (internal guard). Failures
    /// logged and swallowed.
    /// </summary>
    Task NotifyFocusOrderUpdateAsync(OrderDto order);
}
