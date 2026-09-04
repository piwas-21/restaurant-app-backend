namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Sofra's own commission on tenant→diner Stripe payments. Its own file rather than a property on
/// <see cref="StripeSettings"/> because that class is already at the 50-line configuration-class
/// limit (workspace CLAUDE.md §Workspace conventions).
/// </summary>
public class StripeCommissionSettings
{
    public const string SectionName = "Stripe:Commission";

    /// <summary>
    /// Basis points Sofra takes off the top of each charge, e.g. 150 = 1.5%. Default 0 means NO
    /// commission — an absent or zeroed section makes <c>CheckoutCommission.From</c> return
    /// <c>null</c>, and a <c>null</c> fee is what keeps <c>StripeCheckoutClient</c> from setting the
    /// <c>application_fee_amount</c> parameter at all, so every existing tenant's Stripe request
    /// stays byte-identical to before this feature shipped.
    ///
    /// <para>
    /// <c>CheckoutCommission</c> also enforces a ceiling well under 100%. That ceiling exists
    /// because Stripe does not reject an oversized fee — measured 2026-09-04: a confirmed
    /// PaymentIntent for 4000 CHF-minor with a requested fee of 5000 produced an ApplicationFee of
    /// <c>amount=4000</c>, silently capping the fee at 100% of the charge and leaving the
    /// restaurant with nothing, with no error raised anywhere. So this value is validated on our
    /// side before it ever reaches Stripe; Stripe will not do it for us.
    /// </para>
    /// </summary>
    public int Bps { get; set; }
}
