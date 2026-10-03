using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountCheckoutStatusReader(ApplicationDbContext context) : IAccountCheckoutStatusReader
{
    public async Task<AccountCheckoutStartDto> ReadAsync(Guid attemptId, string? canonicalCheckoutUrl,
        CancellationToken cancellationToken)
    {
        var value = await context.AccountCheckoutJournals.AsNoTracking()
            .Join(context.AccountPaymentAttempts.AsNoTracking().Where(attempt => attempt.Id == attemptId),
                journal => journal.AttemptId, attempt => attempt.Id,
                (journal, attempt) => new { Journal = journal, Attempt = attempt })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The original provider contribution is unavailable.");
        var url = value.Attempt.State == AccountPaymentState.Processing
            && !value.Journal.ReconciliationRequired && value.Journal.ProviderCapturedMinor == 0
            ? SafeCheckoutUrl(canonicalCheckoutUrl) : null;
        return new(attemptId, value.Attempt.OperationId, value.Attempt.State, value.Attempt.Version,
            value.Journal.AmountMinor, value.Journal.Currency, value.Journal.ExpiresAt, url,
            value.Journal.ReconciliationRequired, value.Journal.ProviderCapturedMinor, value.Journal.ProviderRefundedMinor);
    }

    internal static string? SafeCheckoutUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && uri.Host.Equals("checkout.stripe.com", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort && uri.UserInfo.Length == 0 ? value : null;
}
