using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService : IFidelityPointsService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPointEarningRuleService _ruleService;

    // Conversion rate: 100 points = $1.00
    private const int PointsPerDollar = 100;

    public FidelityPointsService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IPointEarningRuleService ruleService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _ruleService = ruleService;
    }

    public async Task<int> CalculatePointsForOrderAsync(decimal orderTotal, CancellationToken cancellationToken = default)
    {
        var applicableRule = await _ruleService.FindApplicableRuleAsync(orderTotal, cancellationToken);
        return applicableRule?.PointsAwarded ?? 0;
    }

    public async Task<(FidelityPointsTransaction Transaction, decimal DiscountAmount)> RedeemPointsAsync(
        Guid userId,
        Guid orderId,
        int pointsToRedeem,
        CancellationToken cancellationToken = default)
    {
        if (pointsToRedeem <= 0)
            throw new ArgumentException("Points to redeem must be positive", nameof(pointsToRedeem));

        // Check if there's an existing transaction
        var hasExistingTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasExistingTransaction ? null : await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await LockOrderRowForLoyaltyAsync(orderId, userId, cancellationToken);
            await LockUserRowForLoyaltyAsync(userId, cancellationToken);
            var balance = await LockBalanceRowForLoyaltyAsync(userId, cancellationToken);

            if (balance is { CurrentPoints: < 0 })
                throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
            var outstanding = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
                _context, userId, cancellationToken);
            var spendable = Math.Max(0L, (long)(balance?.CurrentPoints ?? 0) - outstanding);
            if (balance == null || spendable < pointsToRedeem)
            {
                throw new InsufficientPointsException(checked((int)spendable), pointsToRedeem);
            }

            var updatedCurrentPoints = checked(balance.CurrentPoints - pointsToRedeem);
            var updatedTotalRedeemedPoints = checked(balance.TotalRedeemedPoints + pointsToRedeem);
            var now = DateTime.UtcNow;
            var auditIdentifier = _currentUserService.GetAuditIdentifier();

            // Calculate discount amount
            var discountAmount = CalculateDiscountFromPoints(pointsToRedeem);

            // Create transaction record
            var pointsTransaction = new FidelityPointsTransaction
            {
                UserId = userId,
                OrderId = orderId,
                TransactionType = TransactionType.Redeemed,
                Points = -pointsToRedeem, // Negative for redemption
                OrderTotal = null,
                Description = $"Points redeemed for ${discountAmount:F2} discount",
                CreatedAt = now,
                CreatedBy = auditIdentifier
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);

            // Update balance
            balance.CurrentPoints = updatedCurrentPoints;
            balance.TotalRedeemedPoints = updatedTotalRedeemedPoints;
            balance.LastUpdated = now;
            balance.UpdatedAt = now;
            balance.UpdatedBy = auditIdentifier;

            await _context.SaveChangesAsync(cancellationToken);

            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (pointsTransaction, discountAmount);
        }
        catch
        {
            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<FidelityPointBalance?> GetUserBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.FidelityPointBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);
    }

    public async Task<List<FidelityPointsTransaction>> GetPointsHistoryAsync(
        Guid userId,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.FidelityPointsTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<FidelityPointsTransaction> AdjustPointsAsync(
        Guid userId,
        int points,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required for manual adjustments", nameof(reason));

        // Check if there's an existing transaction
        var hasExistingTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasExistingTransaction ? null : await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await LockUserRowForLoyaltyAsync(userId, cancellationToken);
            var balance = await LockBalanceRowForLoyaltyAsync(userId, cancellationToken);

            if (balance is { CurrentPoints: < 0 })
                throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
            var outstanding = await OrderAmendmentLoyaltyReservationEvidence.ReadOutstandingAsync(
                _context, userId, cancellationToken);

            var now = DateTime.UtcNow;
            var currentPoints = balance is null
                ? Math.Max(0, points)
                : Math.Max(0, checked(balance.CurrentPoints + points));
            if (points < 0 && currentPoints < outstanding)
                throw new ConflictException("The adjustment would consume points reserved for an exact amendment clawback.");
            var totalEarnedPoints = balance?.TotalEarnedPoints ?? (points > 0 ? points : 0);
            var totalRedeemedPoints = balance?.TotalRedeemedPoints ?? (points < 0 ? Math.Abs(points) : 0);
            if (balance is not null && points > 0)
            {
                totalEarnedPoints = checked(totalEarnedPoints + points);
            }
            else if (balance is not null && points < 0)
            {
                totalRedeemedPoints = checked(totalRedeemedPoints + Math.Abs(points));
            }

            // Create transaction record
            var pointsTransaction = new FidelityPointsTransaction
            {
                UserId = userId,
                OrderId = null,
                TransactionType = TransactionType.AdminAdjustment,
                Points = points,
                OrderTotal = null,
                Description = reason,
                CreatedAt = now,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);

            // Update or create user's balance under the same user/balance locks as awards and redemptions.
            if (balance == null)
            {
                balance = new FidelityPointBalance
                {
                    UserId = userId,
                    CurrentPoints = currentPoints, // Ensure non-negative
                    TotalEarnedPoints = totalEarnedPoints,
                    TotalRedeemedPoints = totalRedeemedPoints,
                    LastUpdated = now,
                    CreatedAt = now,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.FidelityPointBalances.Add(balance);
            }
            else
            {
                balance.CurrentPoints = currentPoints; // Ensure non-negative
                balance.TotalEarnedPoints = totalEarnedPoints;
                balance.TotalRedeemedPoints = totalRedeemedPoints;

                balance.LastUpdated = now;
                balance.UpdatedAt = now;
                balance.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return pointsTransaction;
        }
        catch
        {
            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            if (!hasExistingTransaction && transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public decimal CalculateDiscountFromPoints(int points)
    {
        // 100 points = $1.00
        return points / (decimal)PointsPerDollar;
    }

    public int CalculatePointsForDiscount(decimal discountAmount)
    {
        // $1.00 = 100 points
        return (int)Math.Ceiling(discountAmount * PointsPerDollar);
    }

    public async Task<SystemAnalytics> GetSystemAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        // Get total points issued (all earned transactions)
        var totalPointsIssued = await _context.FidelityPointsTransactions
            .Where(t => t.TransactionType == TransactionType.Earned || t.TransactionType == TransactionType.AdminAdjustment && t.Points > 0)
            .SumAsync(t => t.Points, cancellationToken);

        // Get total points redeemed
        var totalPointsRedeemed = await _context.FidelityPointsTransactions
            .Where(t => t.TransactionType == TransactionType.Redeemed || t.TransactionType == TransactionType.AdminAdjustment && t.Points < 0)
            .SumAsync(t => Math.Abs(t.Points), cancellationToken);

        // Get total active users with points
        var totalActiveUsers = await _context.FidelityPointBalances
            .Where(b => b.CurrentPoints > 0)
            .CountAsync(cancellationToken);

        // Get total outstanding points
        var totalPointsOutstanding = await _context.FidelityPointBalances
            .SumAsync(b => b.CurrentPoints, cancellationToken);

        // Calculate average points per user
        var averagePointsPerUser = totalActiveUsers > 0
            ? (decimal)totalPointsOutstanding / totalActiveUsers
            : 0;

        // Calculate total discount given (points redeemed converted to dollars)
        var totalDiscountGiven = CalculateDiscountFromPoints(totalPointsRedeemed);

        // Get recent transactions count (last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var recentTransactionsCount = await _context.FidelityPointsTransactions
            .Where(t => t.CreatedAt >= thirtyDaysAgo)
            .CountAsync(cancellationToken);

        return new SystemAnalytics
        {
            TotalPointsIssued = totalPointsIssued,
            TotalPointsRedeemed = totalPointsRedeemed,
            TotalActiveUsers = totalActiveUsers,
            TotalPointsOutstanding = totalPointsOutstanding,
            AveragePointsPerUser = averagePointsPerUser,
            TotalDiscountGiven = totalDiscountGiven,
            RecentTransactionsCount = recentTransactionsCount
        };
    }
}
