using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Interfaces;

public interface ICustomerDiscountService
{
    /// <summary>
    /// Get all active discounts for a specific user
    /// </summary>
    Task<List<CustomerDiscountRule>> GetActiveDiscountsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the best applicable discount for a user's order
    /// </summary>
    Task<CustomerDiscountRule?> FindBestApplicableDiscountAsync(Guid userId, decimal orderAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate the discount amount for a given rule and order amount
    /// </summary>
    decimal CalculateDiscountAmount(CustomerDiscountRule rule, decimal orderAmount);

    /// <summary>
    /// Apply a discount and increment its usage count
    /// </summary>
    Task<CustomerDiscountRule> ApplyDiscountAsync(Guid discountRuleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific discount rule by ID
    /// </summary>
    Task<CustomerDiscountRule?> GetDiscountByIdAsync(Guid discountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all discounts for a user (for admin management)
    /// </summary>
    Task<List<CustomerDiscountRule>> GetAllDiscountsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new customer discount rule
    /// </summary>
    Task<CustomerDiscountRule> CreateDiscountAsync(CustomerDiscountRule discount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing customer discount rule
    /// </summary>
    Task<CustomerDiscountRule> UpdateDiscountAsync(CustomerDiscountRule discount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete (deactivate) a customer discount rule
    /// </summary>
    Task DeleteDiscountAsync(Guid discountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a discount is valid and can be applied
    /// </summary>
    bool IsDiscountValid(CustomerDiscountRule discount, decimal orderAmount);

    /// <summary>
    /// Get count of active customer discounts
    /// </summary>
    Task<int> GetActiveDiscountsCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all customer discounts (admin view)
    /// </summary>
    Task<List<CustomerDiscountRule>> GetAllDiscountsAsync(CancellationToken cancellationToken = default);
}
