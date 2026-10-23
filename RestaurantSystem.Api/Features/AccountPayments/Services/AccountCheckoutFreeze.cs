using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Shared immutable request construction; configuration reads perform no provider I/O.</summary>
internal static class AccountCheckoutFreeze
{
    internal static void RequireReserved(AccountPaymentAttempt attempt, int version, DateTime now)
    {
        if (attempt.PaymentMethod != PaymentMethod.OnlinePayment || attempt.State != AccountPaymentState.Reserved
            || attempt.Version != version || attempt.AmountMinor <= 0
            || attempt.ReservationExpiresAt is not DateTime expires || expires <= now)
            throw new ConflictException("Review and reserve this online contribution before starting checkout.");
    }

    internal static AccountCheckoutJournal Create(AccountPaymentAttempt attempt, string? receiptHash,
        DateTime now, AccountCheckoutSettings settings, IAccountStripeCheckoutClient provider,
        NativeAccountCheckoutScope? native = null, string? nativeReturnBase = null)
    {
        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = attempt.Id,
            AmountMinor = attempt.AmountMinor,
            Currency = attempt.Currency,
            Context = provider.ReadContext(),
            ReturnBaseUrl = native is null ? provider.ReadReturnBaseUrl()
                : nativeReturnBase ?? throw new ConflictException("The original native return path is required."),
            IdempotencyKey = AccountCheckoutReplayPayload.CreateKey(attempt.Id),
            ExpiresAt = now.AddMinutes(settings.CheckoutLifetimeMinutes),
            NativeScope = native
        };
        native?.RequireReturnBase(request.ReturnBaseUrl, attempt.Id);
        return new AccountCheckoutJournal
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            NativeOperationId = native?.OperationId,
            NativeTenantId = native?.TenantId,
            StartedAttemptVersion = attempt.Version,
            AmountMinor = request.AmountMinor,
            Currency = request.Currency,
            ProviderAccountId = request.Context.ConnectedAccountId,
            ProviderLiveMode = request.Context.LiveMode,
            ReturnBaseUrl = request.ReturnBaseUrl,
            CreateIdempotencyKey = request.IdempotencyKey,
            CreatePayloadHash = AccountCheckoutReplayPayload.Hash(request),
            StartedAt = now,
            ExpiresAt = request.ExpiresAt,
            MaximumCreateRetryAt = now.AddHours(settings.MaximumCreateRetryHours),
            NextReconcileAt = now,
            ReceiptCredentialHash = receiptHash,
            ReceiptExpiresAt = receiptHash is null ? null : now.AddHours(settings.ReceiptLifetimeHours),
            CreatedBy = attempt.CreatedBy
        };
    }

    internal static void MarkStarting(AccountPaymentAttempt attempt, AccountCheckoutJournal journal, DateTime now, string audit)
    {
        attempt.State = AccountPaymentState.Starting;
        attempt.StartedAt = now;
        attempt.ProviderAccountId = journal.ProviderAccountId;
        attempt.Version++;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = audit;
    }
}
