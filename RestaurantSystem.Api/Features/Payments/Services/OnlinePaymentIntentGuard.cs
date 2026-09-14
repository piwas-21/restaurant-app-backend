using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>
/// Reads the online intent directly from the database instead of trusting an unloaded or stale
/// <see cref="RestaurantSystem.Domain.Entities.Order.Payments"/> navigation.
/// </summary>
public sealed class OnlinePaymentIntentGuard : IOnlinePaymentIntentGuard
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public OnlinePaymentIntentGuard(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task EnsureProcessingAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var hasProcessingOnlinePayment = await _context.OrderPayments
            .AnyAsync(payment => payment.OrderId == orderId
                && payment.PaymentMethod == PaymentMethod.OnlinePayment
                && payment.Status == PaymentStatus.Processing,
                cancellationToken);

        if (!hasProcessingOnlinePayment)
        {
            throw new BadRequestException(
                "This order does not have a pending online payment intent.");
        }
    }

    public async Task ReactivateLatestFailedAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var auditId = _currentUser.GetAuditIdentifier();
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        if (await _context.OrderPayments.AnyAsync(payment => payment.OrderId == orderId
            && payment.PaymentMethod == PaymentMethod.OnlinePayment
            && payment.Status == PaymentStatus.Processing, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var failed = await _context.OrderPayments
            .AsNoTracking()
            .Where(payment => payment.OrderId == orderId
                && payment.PaymentMethod == PaymentMethod.OnlinePayment
                && payment.Status == PaymentStatus.Failed)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (failed is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var reactivated = await _context.OrderPayments
            .Where(payment => payment.Id == failed.Id && payment.Status == PaymentStatus.Failed)
            .ExecuteUpdateAsync(update => update
                .SetProperty(payment => payment.Status, PaymentStatus.Processing)
                .SetProperty(payment => payment.UpdatedAt, now)
                .SetProperty(payment => payment.UpdatedBy, auditId), cancellationToken);
        if (reactivated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var ownerUpdated = await _context.Orders
            .Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(order => order.Version, order => order.Version + 1)
                .SetProperty(order => order.UpdatedAt, now)
                .SetProperty(order => order.UpdatedBy, auditId), cancellationToken);
        if (ownerUpdated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
