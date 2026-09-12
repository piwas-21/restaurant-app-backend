using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Persists one order tender without changing the caller's replay or award flow.</summary>
internal static class OrderPaymentApplicationPersistence
{
    internal static async Task<PaymentApplicationResult?> ApplyAsync(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOrderPaymentReplayResolver replayResolver,
        Order order,
        OrderPaymentTender tender,
        decimal paymentTolerance,
        CancellationToken cancellationToken)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            // A bill flow supplies its ambient SERIALIZABLE transaction; a till flow gets its own.
            if (context.Database.CurrentTransaction is null)
            {
                transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            }

            var pendingPlaceholders = order.Payments
                .Where(payment => payment.Status == PaymentStatus.Pending)
                .ToList();
            foreach (var placeholder in pendingPlaceholders)
            {
                order.Payments.Remove(placeholder);
                context.OrderPayments.Remove(placeholder);
            }

            var payment = CreatePayment(order, tender, currentUser);
            context.OrderPayments.Add(payment);
            payment.Status = PaymentStatus.Completed;

            // Project around removed placeholders and append the new tender once. EF relationship
            // fixup can otherwise expose the new row in the navigation twice before DetectChanges.
            var summaryPayments = order.Payments
                .Except(pendingPlaceholders)
                .Append(payment)
                .Distinct()
                .ToList();
            RecomputePaymentSummary(order, summaryPayments, currentUser, paymentTolerance);

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

        }
        catch (DbUpdateConcurrencyException)
        {
            return PaymentApplicationResult.VersionConflict();
        }
        catch (Exception ex) when (IsUniqueOperationKeyViolation(ex))
        {
            // PostgreSQL aborts the failed transaction, so dispose it before reading the winner.
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
                transaction = null;
            }

            var winner = await replayResolver.ResolveOutcomeAsync(order.Id, tender, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
        finally
        {
            // The award step must observe no ambient transaction after this method returns.
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return null;
    }

    private static OrderPayment CreatePayment(
        Order order, OrderPaymentTender tender, ICurrentUserService currentUser) => new()
        {
            OrderId = order.Id,
            PaymentMethod = tender.PaymentMethod,
            Amount = tender.Amount,
            Status = PaymentStatus.Pending,
            OperationId = tender.OperationId,
            TableBillPaymentOperationId = tender.TableBillPaymentOperationId,
            TransactionId = tender.TransactionId,
            ReferenceNumber = tender.ReferenceNumber,
            CardLastFourDigits = tender.CardLastFourDigits,
            CardType = tender.CardType,
            PaymentNotes = tender.PaymentNotes,
            Currency = tender.Currency,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetAuditIdentifier()
        };

    private static bool IsUniqueOperationKeyViolation(Exception ex) =>
        ex switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } direct
                when direct.ConstraintName?.Contains("operation_id") == true => true,
            DbUpdateException { InnerException: PostgresException wrapped }
                when wrapped.SqlState == PostgresErrorCodes.UniqueViolation
                && wrapped.ConstraintName?.Contains("operation_id") == true => true,
            _ => false
        };

    private static void RecomputePaymentSummary(
        Order order,
        IReadOnlyCollection<OrderPayment> payments,
        ICurrentUserService currentUser,
        decimal paymentTolerance)
    {
        var capturedPayments = payments.Where(payment => payment.Status.IsCaptured()).Sum(payment => payment.Amount);
        var refundedAmounts = payments.Where(payment => payment.RefundedAmount.HasValue)
            .Sum(payment => payment.RefundedAmount ?? 0);

        order.TotalPaid = capturedPayments - refundedAmounts;
        order.RemainingAmount = order.Total - order.TotalPaid;

        if (order.RemainingAmount > paymentTolerance)
        {
            order.PaymentStatus = order.TotalPaid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending;
        }
        else if (order.RemainingAmount <= -paymentTolerance)
        {
            order.PaymentStatus = PaymentStatus.Overpaid;
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = currentUser.GetAuditIdentifier();
    }
}
