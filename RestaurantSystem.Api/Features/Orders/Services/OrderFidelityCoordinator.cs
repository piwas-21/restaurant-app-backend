using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderFidelityCoordinator : IOrderFidelityCoordinator
{
    private readonly IFidelityPointsService _fidelityPointsService;
    private readonly IOrderPricingService _pricingService;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderFidelityCoordinator> _logger;

    public OrderFidelityCoordinator(
        IFidelityPointsService fidelityPointsService,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        ApplicationDbContext context,
        ILogger<OrderFidelityCoordinator> logger)
    {
        _fidelityPointsService = fidelityPointsService;
        _pricingService = pricingService;
        _paymentBuilder = paymentBuilder;
        _context = context;
        _logger = logger;
    }

    public async Task CalculatePointsToEarnAsync(
        Order order, decimal itemsTotal, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue)
        {
            return;
        }

        var pointsToEarn = await _fidelityPointsService.CalculatePointsForOrderAsync(itemsTotal, cancellationToken);
        order.FidelityPointsEarned = pointsToEarn;

        _logger.LogInformation("Order will earn {Points} fidelity points", pointsToEarn);
    }

    public async Task RedeemAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || !pointsToRedeem.HasValue || pointsToRedeem.Value <= 0)
        {
            return;
        }

        try
        {
            var (_, discountAmount) = await _fidelityPointsService.RedeemPointsAsync(
                userId.Value,
                order.Id, // Order must exist in DB by now (caller saves first to avoid FK violation).
                pointsToRedeem.Value,
                cancellationToken);

            order.FidelityPointsRedeemed = pointsToRedeem.Value;
            order.FidelityPointsDiscount = discountAmount;

            // The credit only becomes knowable here — redemption FKs the order, so it cannot run
            // until after the insert, and the Total already persisted does not reflect it. Reprice
            // from the order's own columns rather than by subtracting a delta, so this is safe to
            // run more than once.
            _pricingService.RecalculateTotal(order);
            _paymentBuilder.UpdatePaymentSummary(order);

            _logger.LogInformation(
                "Redeemed {Points} fidelity points for ${Discount} discount on order {OrderNumber}",
                pointsToRedeem.Value, discountAmount, order.OrderNumber);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: the customer can contact support if redemption failed. The fields above
            // are set on a TRACKED entity, so leaving them would let the next SaveChangesAsync in
            // this transaction — AwardEarnedPointsAsync's, for instance — flush a discount for
            // points that may never have been taken. Roll them back and reprice.
            //
            // ⚠️ This makes the ORDER consistent, not necessarily the LEDGER. RedeemPointsAsync
            // saves inside the handler's ambient transaction, so if IT succeeded and the save on
            // line 76 threw, the balance decrement is already flushed and gets committed with the
            // order: the customer pays full price AND loses the points. That is the deliberate
            // direction — the alternative, leaving the discount in place, gives away food for
            // points that may not have been taken. A points balance is repairable by support; a
            // wrong charge is not. If this ever fires in anger, the log below is the audit trail.
            order.FidelityPointsRedeemed = 0;
            order.FidelityPointsDiscount = 0;
            _pricingService.RecalculateTotal(order);
            _paymentBuilder.UpdatePaymentSummary(order);

            _logger.LogError(ex, "Failed to redeem fidelity points for order {OrderNumber}", order.OrderNumber);
        }
    }

    public async Task AwardEarnedPointsAsync(
        Order order, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || order.FidelityPointsEarned <= 0)
        {
            return;
        }

        // The gate is the ORDER's PaymentStatus, not its tenders'. Every tender created with an
        // order is Pending, and since S0b order.Total is computed server-side from the order's own
        // items — so a caller can no longer declare `basketTotal: 0`, land RemainingAmount at 0,
        // and have points awarded for an order nobody paid for. Both halves are load-bearing:
        // do not weaken this gate, and do not let a client-supplied total back into pricing.
        if (order.PaymentStatus != PaymentStatus.Completed &&
            order.PaymentStatus != PaymentStatus.Overpaid)
        {
            return;
        }

        try
        {
            await _fidelityPointsService.AwardPointsAsync(
                userId.Value,
                order.Id,
                order.FidelityPointsEarned,
                order.SubTotal,
                cancellationToken);

            _logger.LogInformation(
                "Awarded {Points} fidelity points to user {UserId} for order {OrderNumber}",
                order.FidelityPointsEarned, userId, order.OrderNumber);
        }
        catch (Exception ex)
        {
            // Best-effort: order is already created, the points-award failure
            // shouldn't take it down.
            _logger.LogError(
                ex, "Failed to award fidelity points for order {OrderNumber}, but order was created successfully",
                order.OrderNumber);
        }
    }
}
