using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public class FidelityPointsService : IFidelityPointsService
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

    public async Task<FidelityPointsTransaction> AwardPointsAsync(
        Guid userId,
        Guid orderId,
        int points,
        decimal orderTotal,
        CancellationToken cancellationToken = default)
    {
        if (points <= 0)
            throw new ArgumentException("Points must be positive", nameof(points));

        // Check if there's an existing transaction
        var hasExistingTransaction = _context.Database.CurrentTransaction != null;
        var transaction = hasExistingTransaction ? null : await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create transaction record
            var pointsTransaction = new FidelityPointsTransaction
            {
                UserId = userId,
                OrderId = orderId,
                TransactionType = TransactionType.Earned,
                Points = points,
                OrderTotal = orderTotal,
                Description = $"Points earned from order",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId.ToString()
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);

            // Update or create user's balance
            var balance = await _context.FidelityPointBalances
                .FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);

            if (balance == null)
            {
                balance = new FidelityPointBalance
                {
                    UserId = userId,
                    CurrentPoints = points,
                    TotalEarnedPoints = points,
                    TotalRedeemedPoints = 0,
                    LastUpdated = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId.ToString()
                };
                _context.FidelityPointBalances.Add(balance);
            }
            else
            {
                balance.CurrentPoints += points;
                balance.TotalEarnedPoints += points;
                balance.LastUpdated = DateTime.UtcNow;
                balance.UpdatedAt = DateTime.UtcNow;
                balance.UpdatedBy = _currentUserService.UserId?.ToString() ?? userId.ToString();
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
            // Get user's current balance
            var balance = await _context.FidelityPointBalances
                .FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);

            if (balance == null || balance.CurrentPoints < pointsToRedeem)
            {
                throw new BadRequestException($"Insufficient points. Available: {balance?.CurrentPoints ?? 0}, Requested: {pointsToRedeem}");
            }

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
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId.ToString()
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);

            // Update balance
            balance.CurrentPoints -= pointsToRedeem;
            balance.TotalRedeemedPoints += pointsToRedeem;
            balance.LastUpdated = DateTime.UtcNow;
            balance.UpdatedAt = DateTime.UtcNow;
            balance.UpdatedBy = _currentUserService.UserId?.ToString() ?? userId.ToString();

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
            // Create transaction record
            var pointsTransaction = new FidelityPointsTransaction
            {
                UserId = userId,
                OrderId = null,
                TransactionType = TransactionType.AdminAdjustment,
                Points = points,
                OrderTotal = null,
                Description = reason,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);

            // Update or create user's balance
            var balance = await _context.FidelityPointBalances
                .FirstOrDefaultAsync(b => b.UserId == userId, cancellationToken);

            if (balance == null)
            {
                balance = new FidelityPointBalance
                {
                    UserId = userId,
                    CurrentPoints = Math.Max(0, points), // Ensure non-negative
                    TotalEarnedPoints = points > 0 ? points : 0,
                    TotalRedeemedPoints = points < 0 ? Math.Abs(points) : 0,
                    LastUpdated = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.FidelityPointBalances.Add(balance);
            }
            else
            {
                balance.CurrentPoints = Math.Max(0, balance.CurrentPoints + points); // Ensure non-negative
                if (points > 0)
                    balance.TotalEarnedPoints += points;
                else
                    balance.TotalRedeemedPoints += Math.Abs(points);

                balance.LastUpdated = DateTime.UtcNow;
                balance.UpdatedAt = DateTime.UtcNow;
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
