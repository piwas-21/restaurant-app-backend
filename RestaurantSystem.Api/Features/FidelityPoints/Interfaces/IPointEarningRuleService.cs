using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Interfaces;

public interface IPointEarningRuleService
{
    /// <summary>
    /// Get all active point earning rules ordered by priority
    /// </summary>
    Task<List<PointEarningRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all rules (active and inactive) for admin management
    /// </summary>
    Task<List<PointEarningRule>> GetAllRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the applicable earning rule for a given order amount
    /// </summary>
    Task<PointEarningRule?> FindApplicableRuleAsync(decimal orderAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific rule by ID
    /// </summary>
    Task<PointEarningRule?> GetRuleByIdAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new point earning rule
    /// </summary>
    Task<PointEarningRule> CreateRuleAsync(PointEarningRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing point earning rule
    /// </summary>
    Task<PointEarningRule> UpdateRuleAsync(PointEarningRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a point earning rule
    /// </summary>
    Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate that a rule doesn't overlap with existing rules
    /// </summary>
    Task<bool> ValidateNoOverlapAsync(PointEarningRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of active point earning rules
    /// </summary>
    Task<int> GetActiveRulesCountAsync(CancellationToken cancellationToken = default);
}
