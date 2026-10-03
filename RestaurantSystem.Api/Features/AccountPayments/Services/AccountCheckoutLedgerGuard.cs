using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCheckoutLedgerGuard
{
    internal static async Task RequireReconciledAsync(
        ApplicationDbContext context, Guid sessionId, CancellationToken cancellationToken, Guid? verifiedAttemptId = null)
    {
        if (await context.AccountCheckoutJournals.AsNoTracking()
                .Where(value => value.ReconciliationRequired
                    && (!verifiedAttemptId.HasValue || value.AttemptId != verifiedAttemptId.Value))
                .Join(context.AccountPaymentAttempts.AsNoTracking().Where(value => value.ServiceSessionId == sessionId),
                    journal => journal.AttemptId, attempt => attempt.Id, (journal, attempt) => journal.Id)
                .AnyAsync(cancellationToken))
            throw new ConflictException("A provider contribution requires reconciliation before collecting or closing this visit.");
    }
}
