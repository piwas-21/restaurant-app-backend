using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class CheckoutChargeResolver : ICheckoutChargeResolver
{
    private readonly LocalizationSettings _localization;
    private readonly StripeCommissionSettings _commission;

    public CheckoutChargeResolver(
        IOptions<LocalizationSettings> localization,
        IOptions<StripeCommissionSettings> commission)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(commission);

        _localization = localization.Value;
        _commission = commission.Value;
    }

    public CheckoutCharge Resolve(decimal orderTotal)
    {
        var amount = CheckoutAmount.From(orderTotal, _localization.Currency);

        // Order matters: the fee is a percentage of the ALREADY-VALIDATED amount, never of the raw
        // total, so it inherits every guarantee CheckoutAmount.From just established rather than
        // re-deriving them. Null on the fleet default — see CheckoutCommission for why null and not
        // zero is what keeps a non-commission tenant's Stripe request unchanged.
        return new CheckoutCharge(amount, CheckoutCommission.From(amount, _commission.Bps));
    }
}
