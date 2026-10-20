using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Recovery always replays the exact original request within Stripe's retention window.</summary>
public static class CustomerOrderCheckoutReplay
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(31);
    public static readonly TimeSpan MaximumReplayLifetime = TimeSpan.FromHours(23);
    public static string Key(Guid operationId) => $"sofra:native-order:v1:{operationId:D}";
    public static string Hash(CheckoutSessionRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    public static CheckoutSessionRequest Read(CustomerOrderCheckoutJournal journal, DateTime now, bool forCreate)
    {
        if (journal.OrderId == Guid.Empty || journal.OperationId == Guid.Empty
            || journal.OrderNumber.Length is < 1 or > 20 || journal.OrderNumber.Contains('\0')
            || journal.AmountMinor <= 0 || journal.Currency.Length != 3
            || journal.Currency.Any(value => !char.IsAsciiLetterLower(value))
            || journal.ApplicationFeeMinor is <= 0 || journal.ApplicationFeeMinor >= journal.AmountMinor
            || journal.ProviderAccountId.Length > 255 || !journal.ProviderAccountId.StartsWith("acct_", StringComparison.Ordinal)
            || journal.CreateIdempotencyKey != Key(journal.OperationId)
            || journal.StartedAt.Kind != DateTimeKind.Utc || journal.ExpiresAt.Kind != DateTimeKind.Utc
            || journal.MaximumCreateRetryAt.Kind != DateTimeKind.Utc
            || journal.StartedAt.Ticks % TimeSpan.TicksPerSecond != 0
            || journal.ExpiresAt != journal.StartedAt.Add(SessionLifetime)
            || journal.MaximumCreateRetryAt != journal.StartedAt.Add(MaximumReplayLifetime))
            throw Refusal();
        if (forCreate && (now.Kind != DateTimeKind.Utc || now >= journal.MaximumCreateRetryAt || now < journal.StartedAt
                || journal.ProviderSessionId is not null))
            throw new ConflictException("Read canonical provider evidence before retrying this payment.");
        var routing = new NativeCheckoutRouting(journal.OperationId,
            new(journal.ProviderAccountId, journal.ProviderLiveMode), journal.ReturnUrl);
        try { routing.RequireReturn(journal.OrderId); }
        catch (BadRequestException) { throw Refusal(); }
        var request = new CheckoutSessionRequest
        {
            OrderId = journal.OrderId,
            OrderNumber = journal.OrderNumber,
            Currency = journal.Currency,
            AmountMinor = journal.AmountMinor,
            ApplicationFeeMinor = journal.ApplicationFeeMinor,
            ExpiresAt = journal.ExpiresAt,
            IdempotencyKey = journal.CreateIdempotencyKey,
            NativeRouting = routing
        };
        if (!string.Equals(Hash(request), journal.CreatePayloadHash, StringComparison.Ordinal)) throw Refusal();
        return request;
    }

    private static ConflictException Refusal() => new("The frozen payment request requires reconciliation.");
}
