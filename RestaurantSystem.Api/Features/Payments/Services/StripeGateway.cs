using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using Stripe;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class StripeGateway : IStripeGateway
{
    private readonly StripeSettings _settings;
    private readonly IStripeClient? _client;

    public StripeGateway(IOptions<StripeSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.Value;

        // Constructed once, at startup, and only when there is a key to construct it with.
        // StripeClient with an empty key would build fine and fail at the first call with a 401 —
        // i.e. at checkout, in front of a diner. Not building it is what makes IsConfigured honest.
        _client = IsConfigured ? new StripeClient(_settings.PlatformApiKey) : null;
    }

    public bool IsConfigured =>
        _settings.Enabled
        && !string.IsNullOrWhiteSpace(_settings.PlatformApiKey)
        && !string.IsNullOrWhiteSpace(_settings.ConnectedAccountId);

    public string ConnectedAccountId => _settings.ConnectedAccountId;

    public IStripeClient Client =>
        _client ?? throw new BadRequestException("Online payment is not available for this restaurant.");

    public RequestOptions BuildRequestOptions(string? idempotencyKey = null)
    {
        if (!IsConfigured)
        {
            // Deliberately the same user-facing sentence as Client above: a caller that forgot to
            // check IsConfigured gets a refusal a diner can read, not a Stripe 401 or a null-ref.
            throw new BadRequestException("Online payment is not available for this restaurant.");
        }

        return new RequestOptions
        {
            // The supported way to act on a connected account. Connect OAuth's account-scoped
            // access_token is deprecated — quoted in plan §4 — so this header plus the platform key
            // is the whole mechanism.
            StripeAccount = _settings.ConnectedAccountId,
            IdempotencyKey = idempotencyKey,
        };
    }
}
