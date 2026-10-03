using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Expiry-bound, contribution-only recovery remains available after the table visit ends.</summary>
public sealed class AccountPaymentReceiptReader(ApplicationDbContext context, TimeProvider timeProvider)
    : IAccountPaymentReceiptReader
{
    public async Task<AccountPaymentReceiptDto> ReadAsync(
        Guid attemptId, string? receiptCredential, CancellationToken cancellationToken)
    {
        if (attemptId == Guid.Empty || !AccountReceiptCredentialCrypto.TryHash(receiptCredential, out _))
            throw Unavailable();
        var receipt = await context.AccountCheckoutJournals.AsNoTracking()
            .Where(value => value.AttemptId == attemptId)
            .Join(context.AccountPaymentAttempts.AsNoTracking(), value => value.AttemptId,
                attempt => attempt.Id, (value, attempt) => new
                {
                    value.ReceiptCredentialHash,
                    value.ReceiptExpiresAt,
                    value.AmountMinor,
                    value.Currency,
                    value.ProviderCapturedMinor,
                    value.ProviderRefundedMinor,
                    value.ReconciliationRequired,
                    State = attempt.State,
                    CompletedAt = attempt.CompletedAt
                }).SingleOrDefaultAsync(cancellationToken);
        if (receipt is null || !receipt.ReceiptExpiresAt.HasValue
            || receipt.ReceiptExpiresAt <= timeProvider.GetUtcNow().UtcDateTime
            || !AccountReceiptCredentialCrypto.Verify(receiptCredential, receipt.ReceiptCredentialHash))
            throw Unavailable();
        return new AccountPaymentReceiptDto(attemptId, receipt.AmountMinor, receipt.Currency, receipt.State,
            receipt.ProviderCapturedMinor, receipt.ProviderRefundedMinor,
            receipt.ReconciliationRequired, receipt.CompletedAt);
    }

    private static NotFoundException Unavailable() => new("The payment receipt is unavailable.");
}
