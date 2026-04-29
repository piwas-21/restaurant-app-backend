using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Order-related email notifications. Wraps <c>IEmailService</c> with
/// the per-order composition logic (items, delivery address, fallback
/// names) that previously lived inline in <c>OrderEmailController</c>
/// and <c>CreateOrderCommandHandler</c>.
///
/// Extracted in Sprint 2 task 2.10. Also closes
/// <see href="https://gitlab.com/restaurant-app3282120/backend/-/issues/13">#13</see>:
/// the admin fire-and-forget path now resolves a fresh DI scope so it
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
}
