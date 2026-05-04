using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Builds <see cref="OrderPayment"/>s from <see cref="CreateOrderPaymentDto"/>s,
/// appends them to an order, and recomputes the order's payment summary
/// (TotalPaid / RemainingAmount / PaymentStatus).
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.11.
/// </summary>
public interface IOrderPaymentBuilder
{
    /// <summary>
    /// For each payment DTO: build a new <see cref="OrderPayment"/>, mark
    /// non-Cash methods as Completed (Cash stays Pending until explicitly
    /// completed via the AddPaymentToOrder endpoint), and append to
    /// <c>order.Payments</c>.
    /// </summary>
    void AddPayments(Order order, IReadOnlyCollection<CreateOrderPaymentDto> payments);

    /// <summary>
    /// Recomputes the order's <c>TotalPaid</c>, <c>RemainingAmount</c>, and
    /// <c>PaymentStatus</c> based on the currently-attached payments.
    /// Uses a 1-cent tolerance for floating-point precision in the
    /// fully-paid / overpaid determination.
    /// </summary>
    void UpdatePaymentSummary(Order order);
}
