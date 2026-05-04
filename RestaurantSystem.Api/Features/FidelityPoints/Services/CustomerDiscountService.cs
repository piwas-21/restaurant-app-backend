using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public class CustomerDiscountService : ICustomerDiscountService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CustomerDiscountService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<List<CustomerDiscountRule>> GetActiveDiscountsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.CustomerDiscountRules
            .AsNoTracking()
            .Where(d => d.UserId == userId
                && d.IsActive
                && (d.ValidFrom == null || d.ValidFrom <= now)
                && (d.ValidUntil == null || d.ValidUntil >= now)
                && (d.MaxUsageCount == null || d.UsageCount < d.MaxUsageCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerDiscountRule?> FindBestApplicableDiscountAsync(
        Guid userId,
        decimal orderAmount,
        CancellationToken cancellationToken = default)
    {
        var activeDiscounts = await GetActiveDiscountsForUserAsync(userId, cancellationToken);
        var applicableDiscounts = activeDiscounts
            .Where(d => IsDiscountValid(d, orderAmount))
            .ToList();

        // Also fetch applicable group discounts
        var groupDiscounts = await GetApplicableGroupDiscountsAsync(userId, orderAmount, cancellationToken);

        // Combine and find best
        var bestIndividual = applicableDiscounts
            .OrderByDescending(d => CalculateDiscountAmount(d, orderAmount))
            .FirstOrDefault();

        var bestGroup = groupDiscounts
            .OrderByDescending(d => CalculateGroupDiscountAmount(d, orderAmount))
            .FirstOrDefault();

        if (bestIndividual == null && bestGroup == null)
            return null;

        if (bestIndividual == null)
            return MapGroupDiscountToRule(bestGroup!, orderAmount);

        if (bestGroup == null)
            return bestIndividual;

        var individualAmount = CalculateDiscountAmount(bestIndividual, orderAmount);
        var groupAmount = CalculateGroupDiscountAmount(bestGroup, orderAmount);

        return individualAmount >= groupAmount
            ? bestIndividual
            : MapGroupDiscountToRule(bestGroup, orderAmount);
    }

    private async Task<List<GroupDiscount>> GetApplicableGroupDiscountsAsync(
        Guid userId,
        decimal orderAmount,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Get user's active group memberships
        var userGroupIds = await _context.GroupMemberships
            .Where(m => m.UserId == userId && m.IsActive &&
                       (m.ExpiresAt == null || m.ExpiresAt > now))
            .Select(m => m.GroupId)
            .ToListAsync(cancellationToken);

        if (!userGroupIds.Any())
            return new List<GroupDiscount>();

        // Get active discounts for these groups
        var groupDiscounts = await _context.GroupDiscounts
            .Include(gd => gd.Group)
            .Where(gd => userGroupIds.Contains(gd.GroupId) &&
                        gd.IsActive &&
                        gd.Group.IsActive &&
                        (gd.Group.ValidFrom == null || gd.Group.ValidFrom <= now) &&
                        (gd.Group.ValidUntil == null || gd.Group.ValidUntil >= now))
            .ToListAsync(cancellationToken);

        // Filter by order amount
        return groupDiscounts
            .Where(gd => (!gd.MinimumOrderAmount.HasValue || orderAmount >= gd.MinimumOrderAmount.Value))
            .ToList();
    }

    private decimal CalculateGroupDiscountAmount(GroupDiscount discount, decimal orderAmount)
    {
        decimal amount;
        if (discount.Type == DiscountType.Percentage)
        {
            amount = orderAmount * (discount.Value / 100);
        }
        else
        {
            amount = discount.Value;
        }

        // Only cap if MaximumDiscountAmount is set and greater than 0
        // Treating 0 as "no limit" to handle potential data entry errors where 0 was used instead of null
        if (discount.MaximumDiscountAmount.HasValue &&
            discount.MaximumDiscountAmount.Value > 0 &&
            amount > discount.MaximumDiscountAmount.Value)
        {
            amount = discount.MaximumDiscountAmount.Value;
        }

        return amount;
    }

    private CustomerDiscountRule MapGroupDiscountToRule(GroupDiscount groupDiscount, decimal orderAmount)
    {
        // Calculate effective amount to handle caps
        var effectiveAmount = CalculateGroupDiscountAmount(groupDiscount, orderAmount);

        return new CustomerDiscountRule
        {
            Id = groupDiscount.Id,
            Name = $"{groupDiscount.Name} ({groupDiscount.Group.Name})",
            // We map it as a FixedAmount with the calculated effective value
            // This ensures the BasketService applies the exact capped amount
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = effectiveAmount,
            MinOrderAmount = groupDiscount.MinimumOrderAmount,
            IsActive = true,
            UserId = Guid.Empty, // Placeholder
            CreatedBy = "System",
            CreatedAt = DateTime.UtcNow
        };
    }

    public decimal CalculateDiscountAmount(CustomerDiscountRule rule, decimal orderAmount)
    {
        if (rule.DiscountType == DiscountType.Percentage)
        {
            return orderAmount * (rule.DiscountValue / 100);
        }
        else // FixedAmount
        {
            return rule.DiscountValue;
        }
    }

    public async Task<CustomerDiscountRule> ApplyDiscountAsync(
        Guid discountRuleId,
        CancellationToken cancellationToken = default)
    {
        var discount = await _context.CustomerDiscountRules
            .FirstOrDefaultAsync(d => d.Id == discountRuleId, cancellationToken);

        if (discount == null)
            throw new NotFoundException($"Discount rule with ID {discountRuleId} not found");

        // Increment usage count
        discount.UsageCount++;
        discount.UpdatedAt = DateTime.UtcNow;
        discount.UpdatedBy = _currentUserService.GetAuditIdentifier();

        // Deactivate if max usage reached
        if (discount.MaxUsageCount.HasValue && discount.UsageCount >= discount.MaxUsageCount.Value)
        {
            discount.IsActive = false;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return discount;
    }

    public async Task<CustomerDiscountRule?> GetDiscountByIdAsync(
        Guid discountId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CustomerDiscountRules
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discountId, cancellationToken);
    }

    public async Task<List<CustomerDiscountRule>> GetAllDiscountsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CustomerDiscountRules
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerDiscountRule> CreateDiscountAsync(
        CustomerDiscountRule discount,
        CancellationToken cancellationToken = default)
    {
        // Validate discount
        if (discount.DiscountType == DiscountType.Percentage && discount.DiscountValue > 100)
        {
            throw new ArgumentException("Percentage discount cannot exceed 100%");
        }

        if (discount.DiscountValue <= 0)
        {
            throw new ArgumentException("Discount value must be positive");
        }

        discount.CreatedAt = DateTime.UtcNow;
        discount.CreatedBy = _currentUserService.GetAuditIdentifier();
        discount.UsageCount = 0;

        _context.CustomerDiscountRules.Add(discount);
        await _context.SaveChangesAsync(cancellationToken);

        return discount;
    }

    public async Task<CustomerDiscountRule> UpdateDiscountAsync(
        CustomerDiscountRule discount,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.CustomerDiscountRules
            .FirstOrDefaultAsync(d => d.Id == discount.Id, cancellationToken);

        if (existing == null)
            throw new NotFoundException($"Discount rule with ID {discount.Id} not found");

        // Validate discount
        if (discount.DiscountType == DiscountType.Percentage && discount.DiscountValue > 100)
        {
            throw new ArgumentException("Percentage discount cannot exceed 100%");
        }

        if (discount.DiscountValue <= 0)
        {
            throw new ArgumentException("Discount value must be positive");
        }

        existing.Name = discount.Name;
        existing.DiscountType = discount.DiscountType;
        existing.DiscountValue = discount.DiscountValue;
        existing.MinOrderAmount = discount.MinOrderAmount;
        existing.MaxOrderAmount = discount.MaxOrderAmount;
        existing.MaxUsageCount = discount.MaxUsageCount;
        existing.IsActive = discount.IsActive;
        existing.ValidFrom = discount.ValidFrom;
        existing.ValidUntil = discount.ValidUntil;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return existing;
    }

    public async Task DeleteDiscountAsync(Guid discountId, CancellationToken cancellationToken = default)
    {
        var discount = await _context.CustomerDiscountRules
            .FirstOrDefaultAsync(d => d.Id == discountId, cancellationToken);

        if (discount == null)
            throw new NotFoundException($"Discount rule with ID {discountId} not found");

        // Soft delete by deactivating
        discount.IsActive = false;
        discount.UpdatedAt = DateTime.UtcNow;
        discount.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);
    }

    public bool IsDiscountValid(CustomerDiscountRule discount, decimal orderAmount)
    {
        var now = DateTime.UtcNow;

        // Check if active
        if (!discount.IsActive)
            return false;

        // Check date validity
        if (discount.ValidFrom.HasValue && discount.ValidFrom.Value > now)
            return false;

        if (discount.ValidUntil.HasValue && discount.ValidUntil.Value < now)
            return false;

        // Check usage count
        if (discount.MaxUsageCount.HasValue && discount.UsageCount >= discount.MaxUsageCount.Value)
            return false;

        // Check order amount constraints
        if (discount.MinOrderAmount.HasValue && orderAmount < discount.MinOrderAmount.Value)
            return false;

        if (discount.MaxOrderAmount.HasValue && orderAmount > discount.MaxOrderAmount.Value)
            return false;

        return true;
    }

    public async Task<int> GetActiveDiscountsCountAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.CustomerDiscountRules
            .Where(d => d.IsActive &&
                       (!d.ValidFrom.HasValue || d.ValidFrom.Value <= now) &&
                       (!d.ValidUntil.HasValue || d.ValidUntil.Value >= now))
            .CountAsync(cancellationToken);
    }

    public async Task<List<CustomerDiscountRule>> GetAllDiscountsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CustomerDiscountRules
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
