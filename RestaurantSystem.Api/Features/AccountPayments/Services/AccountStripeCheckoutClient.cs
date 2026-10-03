using System.Globalization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Direct-charge checkout for one frozen contribution, independent of order checkout.</summary>
public sealed class AccountStripeCheckoutClient(
    IStripeGateway gateway,
    IOptions<StripeSettings> stripeOptions,
    IOptions<EmailSettings> emailOptions,
    IOptions<AccountCheckoutSettings> checkoutOptions) : IAccountStripeCheckoutClient
{
    public const string AttemptMetadataKey = "account_payment_attempt";
    public const string SchemaMetadataKey = "sofra_payment_schema";
    public const string SchemaVersion = "account-payment-v1";

    public AccountStripeContext ReadContext()
    {
        if (!gateway.IsConfigured)
            throw new BadRequestException("Online payment is not available for this restaurant.");
        var key = stripeOptions.Value.PlatformApiKey;
        var liveMode = key.StartsWith("sk_live_", StringComparison.Ordinal)
            || key.StartsWith("rk_live_", StringComparison.Ordinal);
        var testMode = key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.StartsWith("rk_test_", StringComparison.Ordinal);
        if (!liveMode && !testMode)
            throw new BadRequestException("The online payment environment requires configuration.");
        return new AccountStripeContext(gateway.ConnectedAccountId, liveMode);
    }

    public async Task<AccountStripeSession> CreateAsync(
        AccountStripeCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (request.Context != ReadContext())
            throw new ConflictException("The payment account or environment changed. Reconcile the original attempt.");
        if (request.AttemptId == Guid.Empty || request.AmountMinor <= 0
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.Currency.Length != 3 || request.Currency.Any(value => !char.IsAsciiLetter(value)))
            throw new BadRequestException("A frozen contribution and safe provider replay key are required.");
        var attemptId = request.AttemptId.ToString("D", CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>
        {
            [AttemptMetadataKey] = attemptId,
            [SchemaMetadataKey] = SchemaVersion
        };
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = attemptId,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
            ExpiresAt = request.ExpiresAt,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = request.AmountMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Table account contribution"
                        }
                    }
                }
            ],
            SuccessUrl = BuildReturnUrl(request.ReturnBaseUrl, attemptId, canceled: false),
            CancelUrl = BuildReturnUrl(request.ReturnBaseUrl, attemptId, canceled: true)
        };
        var session = await new SessionService(gateway.Client).CreateAsync(
            options, gateway.BuildRequestOptions(request.IdempotencyKey), cancellationToken);
        return Map(session);
    }

    public async Task<AccountStripeSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await new SessionService(gateway.Client).GetAsync(
                sessionId, options: null, gateway.BuildRequestOptions(), cancellationToken);
            return Map(session);
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
        {
            return null;
        }
    }

    public async Task<AccountStripeIntent?> GetIntentAsync(string intentId, CancellationToken cancellationToken)
    {
        try
        {
            var intent = await new PaymentIntentService(gateway.Client).GetAsync(
                intentId, options: null, gateway.BuildRequestOptions(), cancellationToken);
            return new AccountStripeIntent
            {
                Id = intent.Id,
                Context = new AccountStripeContext(gateway.ConnectedAccountId, intent.Livemode),
                Status = intent.Status ?? string.Empty,
                AmountMinor = intent.Amount,
                ReceivedMinor = intent.AmountReceived,
                Currency = intent.Currency ?? string.Empty,
                ChargeId = intent.LatestChargeId,
                Metadata = intent.Metadata ?? new Dictionary<string, string>()
            };
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
        {
            return null;
        }
    }

    public async Task<AccountStripeCharge?> GetChargeAsync(string chargeId, CancellationToken cancellationToken)
    {
        try
        {
            var charge = await new ChargeService(gateway.Client).GetAsync(
                chargeId, options: null, gateway.BuildRequestOptions(), cancellationToken);
            return new AccountStripeCharge
            {
                Id = charge.Id,
                Context = new AccountStripeContext(gateway.ConnectedAccountId, charge.Livemode),
                IntentId = charge.PaymentIntentId ?? string.Empty,
                Status = charge.Status ?? string.Empty,
                AmountMinor = charge.Amount,
                CapturedMinor = charge.AmountCaptured,
                RefundedMinor = charge.AmountRefunded,
                Currency = charge.Currency ?? string.Empty,
                Paid = charge.Paid,
                Captured = charge.Captured,
                Disputed = charge.Disputed
            };
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
        {
            return null;
        }
    }

    public async Task ExpireAsync(string sessionId, CancellationToken cancellationToken)
    {
        await new SessionService(gateway.Client).ExpireAsync(
            sessionId, options: null, gateway.BuildRequestOptions(), cancellationToken);
    }

    private AccountStripeSession Map(Session session) => new()
    {
        Id = session.Id,
        Context = new AccountStripeContext(gateway.ConnectedAccountId, session.Livemode),
        Url = session.Url,
        Status = session.Status ?? string.Empty,
        PaymentStatus = session.PaymentStatus ?? string.Empty,
        AmountMinor = session.AmountTotal,
        Currency = session.Currency,
        IntentId = session.PaymentIntentId,
        ClientReferenceId = session.ClientReferenceId,
        Metadata = session.Metadata ?? new Dictionary<string, string>()
    };

    public string ReadReturnBaseUrl()
    {
        var path = checkoutOptions.Value.ReturnPath;
        var origin = emailOptions.Value.FrontendBaseUrl;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/"
            || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('?') || path.Contains('#') || path.Contains('\\'))
            throw new BadRequestException("The table payment return path requires configuration.");
        var result = $"{origin.TrimEnd('/')}{path}";
        RequireReturnBaseUrl(result);
        return result;
    }

    private static string BuildReturnUrl(string returnBaseUrl, string attemptId, bool canceled)
    {
        RequireReturnBaseUrl(returnBaseUrl);
        return $"{returnBaseUrl}?paymentAttempt={attemptId}&canceled={(canceled ? "1" : "0")}";
    }

    private static void RequireReturnBaseUrl(string returnBaseUrl)
    {
        if (!Uri.TryCreate(returnBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || returnBaseUrl.Contains('\\'))
            throw new BadRequestException("The frozen table payment return URL is invalid.");
    }
}
