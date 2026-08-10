namespace RestaurantSystem.Api.Features.Payments.Dtos;

/// <summary>
/// Whether this restaurant can take an online payment right now, asked by the checkout page
/// BEFORE it offers the option (SOFRA-PAYMENTS-PLAN §5 S8).
///
/// <para>
/// One boolean, deliberately. The endpoint is anonymous, so anything else on it is public —
/// and every richer answer that was considered (which methods, which account, why not) either
/// duplicates what Stripe's own hosted page shows or tells a stranger about the tenant's
/// billing configuration.
/// </para>
/// </summary>
/// <param name="Available">
/// True only when the tenant bought the module AND the Stripe credentials are actually present.
/// A false here is not an error state — it is the answer for every tenant in the fleet today.
/// </param>
public record OnlinePaymentAvailabilityDto(bool Available);
