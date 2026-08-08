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
    /// For each payment DTO: build a new <see cref="OrderPayment"/> with status
    /// <c>Pending</c> and append it to <c>order.Payments</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing here is ever Completed.</b> Order creation has not observed money
    /// changing hands — not even at the till, which completes through the
    /// <c>[RequireStaff]</c> AddPaymentToOrder endpoint, where the transaction
    /// reference and the human who took the payment both live. This method used to
    /// mark non-Cash tenders Completed, which is what made a paid order something an
    /// anonymous caller could simply assert.
    /// </para>
    /// <para>
    /// This is the single chokepoint for creating a payment alongside an order — both
    /// <c>POST /api/Orders</c> and <c>/from-basket</c> reach it — which is why the
    /// tender allow-list lives here rather than in a validator a third caller could
    /// bypass.
    /// </para>
    /// </remarks>
    /// <exception cref="Common.Exceptions.BadRequestException">
    /// A non-staff caller declared a tender other than Cash. This throw IS the
    /// security control: order creation is anonymous, so the declared tender is a
    /// claim, and Cash is the only one that settles somewhere a human verifies it.
    /// </exception>
    void AddPayments(Order order, IReadOnlyCollection<CreateOrderPaymentDto> payments);

    /// <summary>
    /// Recomputes the order's <c>TotalPaid</c>, <c>RemainingAmount</c>, and
    /// <c>PaymentStatus</c> based on the currently-attached payments.
    /// Uses a 1-cent tolerance for floating-point precision in the
    /// fully-paid / overpaid determination.
    /// </summary>
    void UpdatePaymentSummary(Order order);
}
