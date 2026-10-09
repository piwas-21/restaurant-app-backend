using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Consistent quote timing, provider retry limits, currency and sanitized diagnostics.</summary>
public sealed class OrderAmendmentResolutionPolicy(
    TimeProvider clock,
    IOptions<OrderAmendmentResolutionSettings> settings,
    IOrderDisplayCurrencyResolver currencyResolver,
    ILogger<OrderAmendmentResolutionPolicy> logger,
    ITenantFeatures features) : IOrderAmendmentResolutionPolicy
{
    public void RequireFeature() => OrderAmendmentPolicy.RequireFeature(features);

    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    public TimeSpan QuoteLifetime => TimeSpan.FromMinutes(settings.Value.FinancialResolutionQuoteLifetimeMinutes);

    public bool IsProviderRetrySafe(DateTime requestedAt) =>
        UtcNow - requestedAt < TimeSpan.FromHours(settings.Value.ProviderIdempotencySafetyWindowHours);

    public string? ResolveCurrency(Order source) => currencyResolver.Resolve(source);

    public void WarnProviderFailure(Exception failure, Guid legId) =>
        AccountCheckoutDiagnostics.Warn(logger, failure, AccountCheckoutFailurePhase.ProviderRecovery, legId);
}
