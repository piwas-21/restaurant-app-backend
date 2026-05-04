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

        // Cash payments stay Pending until explicitly completed; points are
        // awarded at payment-completion time, not at order-creation time.
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
