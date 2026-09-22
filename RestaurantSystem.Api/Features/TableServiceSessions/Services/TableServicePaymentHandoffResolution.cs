using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public static class TableServicePaymentHandoffResolution
{
    public static async Task ResolveIfSettledAsync(
        ApplicationDbContext context,
        Guid serviceSessionId,
        Guid paymentOperationKey,
        string resolvedBy,
        decimal paymentTolerance,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var handoff = await context.TableServicePaymentHandoffs
            .SingleOrDefaultAsync(value => value.ServiceSessionId == serviceSessionId
                && value.Status == TableServicePaymentHandoffStatus.Requested, cancellationToken);
        if (handoff is null)
        {
            return;
        }

        var remaining = await TableServicePaymentHandoffRules.ReadOutstandingAsync(
            context, serviceSessionId, cancellationToken);
        if (remaining > paymentTolerance)
        {
            return;
        }

        var paymentOperationId = context.ChangeTracker
            .Entries<TableBillPaymentOperation>()
            .Select(entry => entry.Entity)
            .Where(operation => operation.OperationId == paymentOperationKey)
            .Select(operation => operation.Id)
            .SingleOrDefault();
        if (paymentOperationId == Guid.Empty)
        {
            return;
        }

        handoff.Status = TableServicePaymentHandoffStatus.Resolved;
        handoff.ResolvedAt = timeProvider.GetUtcNow().UtcDateTime;
        handoff.ResolvedPaymentOperationId = paymentOperationId;
        handoff.ResolvedBy = resolvedBy;
    }
}
