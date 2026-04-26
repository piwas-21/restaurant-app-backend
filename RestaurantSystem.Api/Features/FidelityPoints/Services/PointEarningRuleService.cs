using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public class PointEarningRuleService : IPointEarningRuleService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public PointEarningRuleService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<List<PointEarningRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PointEarningRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.MinOrderAmount)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PointEarningRule>> GetAllRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PointEarningRules
            .AsNoTracking()
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.MinOrderAmount)
            .ToListAsync(cancellationToken);
    }

    public async Task<PointEarningRule?> FindApplicableRuleAsync(
        decimal orderAmount,
        CancellationToken cancellationToken = default)
    {
        // Find the first matching rule ordered by priority
        return await _context.PointEarningRules
            .AsNoTracking()
            .Where(r => r.IsActive
                && r.MinOrderAmount <= orderAmount
                && (r.MaxOrderAmount == null || r.MaxOrderAmount >= orderAmount))
            .OrderBy(r => r.Priority)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PointEarningRule?> GetRuleByIdAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        return await _context.PointEarningRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
    }

    public async Task<PointEarningRule> CreateRuleAsync(
        PointEarningRule rule,
        CancellationToken cancellationToken = default)
    {
        // Validate no overlap
        if (!await ValidateNoOverlapAsync(rule, cancellationToken))
        {
            throw new BadRequestException(
                $"Rule overlaps with existing rule. Range: ${rule.MinOrderAmount} - ${rule.MaxOrderAmount?.ToString() ?? "unlimited"}");
        }

        rule.CreatedAt = DateTime.UtcNow;
        rule.CreatedBy = _currentUserService.GetAuditIdentifier();

        _context.PointEarningRules.Add(rule);
        await _context.SaveChangesAsync(cancellationToken);

        return rule;
    }

    public async Task<PointEarningRule> UpdateRuleAsync(
        PointEarningRule rule,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.PointEarningRules
            .FirstOrDefaultAsync(r => r.Id == rule.Id, cancellationToken);

        if (existing == null)
            throw new NotFoundException($"Rule with ID {rule.Id} not found");

        // Validate no overlap (excluding current rule)
        if (!await ValidateNoOverlapAsync(rule, cancellationToken))
        {
            throw new BadRequestException(
                $"Updated rule would overlap with existing rule. Range: ${rule.MinOrderAmount} - ${rule.MaxOrderAmount?.ToString() ?? "unlimited"}");
        }

        existing.Name = rule.Name;
        existing.MinOrderAmount = rule.MinOrderAmount;
        existing.MaxOrderAmount = rule.MaxOrderAmount;
        existing.PointsAwarded = rule.PointsAwarded;
        existing.IsActive = rule.IsActive;
        existing.Priority = rule.Priority;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return existing;
    }

    public async Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _context.PointEarningRules
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

        if (rule == null)
            throw new NotFoundException($"Rule with ID {ruleId} not found");

        _context.PointEarningRules.Remove(rule);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ValidateNoOverlapAsync(
        PointEarningRule rule,
        CancellationToken cancellationToken = default)
    {
        // Get all active rules except the current one (if it's an update)
        var existingRules = await _context.PointEarningRules
            .AsNoTracking()
            .Where(r => r.Id != rule.Id && r.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var existing in existingRules)
        {
            // Check for overlap
            var ruleMin = rule.MinOrderAmount;
            var ruleMax = rule.MaxOrderAmount ?? decimal.MaxValue;
            var existingMin = existing.MinOrderAmount;
            var existingMax = existing.MaxOrderAmount ?? decimal.MaxValue;

            // Ranges overlap if:
            // - rule starts within existing range, OR
            // - rule ends within existing range, OR
            // - rule completely contains existing range
            if ((ruleMin >= existingMin && ruleMin <= existingMax) ||
                (ruleMax >= existingMin && ruleMax <= existingMax) ||
                (ruleMin <= existingMin && ruleMax >= existingMax))
            {
                return false; // Overlap detected
            }
        }

        return true; // No overlap
    }

    public async Task<int> GetActiveRulesCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PointEarningRules
            .Where(r => r.IsActive)
            .CountAsync(cancellationToken);
    }
}
