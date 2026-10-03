using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>A lost provider response reuses the original payload; it never rotates the replay key.</summary>
public static class AccountCheckoutReplayPayload
{
    public static string CreateKey(Guid attemptId) => $"sofra:account-payment:v1:{attemptId:D}";

    public static string Hash(AccountStripeCheckoutRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    public static AccountStripeCheckoutRequest Read(AccountCheckoutJournal journal, DateTime now)
    {
        if (journal.AttemptId == Guid.Empty || journal.StartedAttemptVersion <= 0 || journal.AmountMinor <= 0
            || journal.Currency.Length != 3 || journal.Currency.Any(value => !char.IsAsciiLetterUpper(value))
            || !journal.ProviderAccountId.StartsWith("acct_", StringComparison.Ordinal)
            || journal.ExpiresAt <= journal.StartedAt || journal.MaximumCreateRetryAt <= journal.StartedAt
            || journal.MaximumCreateRetryAt > journal.StartedAt.AddHours(23)
            || journal.CreateIdempotencyKey != CreateKey(journal.AttemptId))
            throw new ConflictException("The frozen provider request requires reconciliation.");
        if (now >= journal.MaximumCreateRetryAt || journal.ProviderSessionId is not null)
            throw new ConflictException("Read canonical provider evidence before retrying this contribution.");

        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = journal.AttemptId,
            Context = new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode),
            AmountMinor = journal.AmountMinor,
            Currency = journal.Currency,
            ExpiresAt = journal.ExpiresAt,
            IdempotencyKey = journal.CreateIdempotencyKey,
            ReturnBaseUrl = journal.ReturnBaseUrl
        };
        if (!string.Equals(Hash(request), journal.CreatePayloadHash, StringComparison.Ordinal))
            throw new ConflictException("The frozen provider request changed. Reconciliation is required.");
        return request;
    }
}
