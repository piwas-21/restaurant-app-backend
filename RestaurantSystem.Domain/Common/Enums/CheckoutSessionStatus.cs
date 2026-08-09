namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// Lifecycle of a Stripe hosted-Checkout session. Deliberately NOT mapped onto
/// <see cref="PaymentStatus"/>: that enum describes a tender in our ledger, this one describes a
/// redirect that may never be completed. A session can expire while its tender stays
/// <c>Processing</c> until the reconciler cancels the order.
/// </summary>
public enum CheckoutSessionStatus
{
    /// <summary>Session minted at Stripe; the customer has been redirected but nothing is settled.</summary>
    Created = 1,

    /// <summary>Stripe reported the payment as paid and we have written the tender. Terminal.</summary>
    Completed = 2,

    /// <summary>Passed <c>ExpiresAt</c> without payment. Terminal.</summary>
    Expired = 3,

    /// <summary>Stripe reported a failure, or a settle attempt found an inconsistency. Terminal.</summary>
    Failed = 4
}
