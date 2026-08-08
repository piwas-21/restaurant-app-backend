using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderFidelityCoordinator : IOrderFidelityCoordinator
{
    private readonly IFidelityPointsService _fidelityPointsService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderFidelityCoordinator> _logger;

    public OrderFidelityCoordinator(
        IFidelityPointsService fidelityPointsService,
        ApplicationDbContext context,
        ILogger<OrderFidelityCoordinator> logger)
    {
        _fidelityPointsService = fidelityPointsService;
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

            _logger.LogInformation(
                "Redeemed {Points} fidelity points for ${Discount} discount on order {OrderNumber}",
                pointsToRedeem.Value, discountAmount, order.OrderNumber);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: customer can contact support if redemption failed.
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

        // The gate is the ORDER's PaymentStatus, not its tenders'. Since every tender
        // created with an order is Pending, at creation time this normally returns —
        // but not always: UpdatePaymentSummary derives PaymentStatus from
        // order.Total, and order.Total is still copied verbatim from the caller's
        // BasketTotal (OrderPricingService.ApplyTotal). A declared total of 0 leaves
        // RemainingAmount at 0, which reads as Completed, and points are awarded on
        // an order nobody paid for. Closing that is S0b — server-authoritative
        // totals. Do not weaken this gate on the assumption it is unreachable.
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
