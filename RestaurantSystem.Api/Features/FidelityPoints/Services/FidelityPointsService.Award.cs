using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    public async Task<FidelityPointsTransaction> AwardPointsAsync(
        Guid userId,
        Guid orderId,
        int points,
        decimal orderTotal,
        CancellationToken cancellationToken = default)
    {
        if (points <= 0)
        {
            throw new ArgumentException("Points must be positive", nameof(points));
        }

        var hasCallerTransaction = _context.Database.CurrentTransaction is not null;
        var transaction = hasCallerTransaction
            ? null
            : await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await LockOrderRowForLoyaltyAsync(orderId, userId, cancellationToken);
            await LockUserRowForLoyaltyAsync(userId, cancellationToken);

            var existingAwards = await _context.FidelityPointsTransactions
                .AsNoTracking()
                .Where(value => value.OrderId == orderId
                    && value.TransactionType == TransactionType.Earned)
                .OrderBy(value => value.Id)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (existingAwards.Count > 1)
            {
                throw new ConflictException("Duplicate loyalty awards require reconciliation.");
            }

            if (existingAwards.Count == 1)
            {
                var original = existingAwards[0];
                if (original.UserId != userId || original.Points != points
                    || original.OrderTotal != orderTotal)
                {
                    throw new ConflictException("The original loyalty award does not match this retry.");
                }

                if (await LockBalanceRowForLoyaltyAsync(userId, cancellationToken) is null)
                {
                    throw new ConflictException("The original loyalty award has no balance record.");
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return original;
            }

            var auditIdentifier = _currentUserService.GetAuditIdentifier();
            var balance = await LockBalanceRowForLoyaltyAsync(userId, cancellationToken);
            var now = DateTime.UtcNow;
            if (balance is null)
            {
                balance = new FidelityPointBalance
                {
                    UserId = userId,
                    CurrentPoints = points,
                    TotalEarnedPoints = points,
                    TotalRedeemedPoints = 0,
                    LastUpdated = now,
                    CreatedAt = now,
                    CreatedBy = auditIdentifier
                };
                _context.FidelityPointBalances.Add(balance);
            }
            else
            {
                var updatedCurrentPoints = checked(balance.CurrentPoints + points);
                var updatedTotalEarnedPoints = checked(balance.TotalEarnedPoints + points);
                balance.CurrentPoints = updatedCurrentPoints;
                balance.TotalEarnedPoints = updatedTotalEarnedPoints;
                balance.LastUpdated = now;
                balance.UpdatedAt = now;
                balance.UpdatedBy = auditIdentifier;
            }

            var pointsTransaction = new FidelityPointsTransaction
            {
                UserId = userId,
                OrderId = orderId,
                TransactionType = TransactionType.Earned,
                Points = points,
                OrderTotal = orderTotal,
                Description = "Points earned from order",
                CreatedAt = now,
                CreatedBy = auditIdentifier
            };

            _context.FidelityPointsTransactions.Add(pointsTransaction);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return pointsTransaction;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
