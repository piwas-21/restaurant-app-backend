using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class CustomerDiscountService
{
    public async Task<CustomerDiscountRule> ApplyDiscountAsync(
        Guid discountRuleId, CancellationToken cancellationToken = default)
    {
        // An order owns its transaction. Standalone callers need the same update/read boundary,
        // otherwise a concurrent consumer could replace the value returned by this attempt.
        await using var transaction = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var now = DateTime.UtcNow;
        var auditId = _currentUserService.GetAuditIdentifier();
        // Eligibility and increment are one database statement. A tracked entity, even one loaded
        // by this context, cannot authorize the last use or supply a stale read/modify/write count.
        var changed = await _context.CustomerDiscountRules
            .Where(rule => rule.Id == discountRuleId && rule.IsActive
                && (!rule.ValidFrom.HasValue || rule.ValidFrom.Value <= now)
                && (!rule.ValidUntil.HasValue || rule.ValidUntil.Value >= now)
                && (!rule.MaxUsageCount.HasValue || rule.UsageCount < rule.MaxUsageCount.Value)
                && rule.UsageCount >= 0 && rule.UsageCount < int.MaxValue)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rule => rule.UsageCount, rule => rule.UsageCount + 1)
                .SetProperty(rule => rule.IsActive, rule => !rule.MaxUsageCount.HasValue
                    || rule.UsageCount + 1 < rule.MaxUsageCount.Value)
                .SetProperty(rule => rule.UpdatedAt, now)
                .SetProperty(rule => rule.UpdatedBy, auditId), cancellationToken);
        if (changed == 0)
        {
            if (!await _context.CustomerDiscountRules.AnyAsync(rule => rule.Id == discountRuleId, cancellationToken))
                throw new NotFoundException($"Discount rule with ID {discountRuleId} not found");
            throw new ConflictException("The customer discount is no longer available. Review the order again.");
        }
        var applied = await _context.CustomerDiscountRules.AsNoTracking()
            .SingleAsync(rule => rule.Id == discountRuleId, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return applied;
    }
}
