using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Interfaces;

public interface IFidelityPointsService
{
    /// <summary>
    /// Calculate points that should be awarded for an order based on active earning rules
    /// </summary>
    Task<int> CalculatePointsForOrderAsync(decimal orderTotal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Award points to a user for an order
    /// </summary>
    Task<FidelityPointsTransaction> AwardPointsAsync(Guid userId, Guid orderId, int points, decimal orderTotal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeem points for a discount on an order
    /// </summary>
    Task<(FidelityPointsTransaction Transaction, decimal DiscountAmount)> RedeemPointsAsync(Guid userId, Guid orderId, int pointsToRedeem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's current fidelity point balance
    /// </summary>
    Task<FidelityPointBalance?> GetUserBalanceAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's fidelity points transaction history
    /// </summary>
    Task<List<FidelityPointsTransaction>> GetPointsHistoryAsync(Guid userId, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin function to manually adjust user's points (for corrections, promotions, etc.)
    /// </summary>
    Task<FidelityPointsTransaction> AdjustPointsAsync(Guid userId, int points, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate discount amount based on points to be redeemed (e.g., 100 points = $1)
    /// </summary>
    decimal CalculateDiscountFromPoints(int points);

    /// <summary>
    /// Calculate points needed for a specific discount amount
    /// </summary>
    int CalculatePointsForDiscount(decimal discountAmount);

    /// <summary>
    /// Get system-wide analytics for fidelity points
    /// </summary>
    Task<SystemAnalytics> GetSystemAnalyticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// System analytics for fidelity points
/// </summary>
public class SystemAnalytics
{
    public int TotalPointsIssued { get; set; }
    public int TotalPointsRedeemed { get; set; }
    public int TotalActiveUsers { get; set; }
    public int TotalPointsOutstanding { get; set; }
    public decimal AveragePointsPerUser { get; set; }
    public decimal TotalDiscountGiven { get; set; }
    public int RecentTransactionsCount { get; set; }
}
