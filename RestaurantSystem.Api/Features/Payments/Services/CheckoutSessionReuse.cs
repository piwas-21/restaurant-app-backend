using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class CheckoutSessionReuse : ICheckoutSessionReuse
{
    private readonly ApplicationDbContext _context;
    private readonly IStripeCheckoutClient _checkout;

    public CheckoutSessionReuse(ApplicationDbContext context, IStripeCheckoutClient checkout)
    {
        _context = context;
        _checkout = checkout;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Stripe is asked, never the local row alone: with no webhook in v1 (plan §4) our
    /// <c>Created</c> is only a claim about the past, and the row can be arbitrarily stale.
    /// </remarks>
    public async Task<CheckoutSessionDto?> TryReuseAsync(
        IReadOnlyCollection<OrderCheckoutSession> sessions,
        CheckoutAmount amount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var live = sessions.FirstOrDefault(s => s.Status == CheckoutSessionStatus.Created);
        if (live is null) return null;

        var remote = await _checkout.GetAsync(live.SessionId, cancellationToken);

        // COMPLETE, not paid, is the test. A delayed-notification method (SEPA, Klarna, Sofort —
        // all reachable because Stripe picks methods dynamically) completes with payment_status
        // still `unpaid` while funds clear. Keyed off IsPaid alone, that reads as "not paid yet",
        // and this would expire a session the diner has already been through and mint a second one
        // for the same amount. The row is left exactly as it is: settling belongs to S5, and
        // half-settling here would put a second writer on a transition that must happen once.
        if (remote?.IsComplete == true || remote?.IsPaid == true)
        {
            throw new BadRequestException("A payment for this order is already in progress.");
        }

        // Destructured rather than `remote?.IsOpen == true` + `remote.Url!`: IsOpen already implies a
        // URL, but only the pattern makes that visible to the compiler instead of asserted at it.
        if (remote is { IsOpen: true, Url: { } liveUrl })
        {
            // A live session cannot be cancelled from here, so a mismatch must REFUSE rather than
            // mint a replacement — a second page while the first is still payable is the double-pay
            // this method exists to prevent. Today order.Total is written once at creation, so this
            // is insurance, not a known path.
            if (live.AmountMinor != amount.Minor || live.Currency != amount.Currency)
            {
                throw new BadRequestException("A payment for this order is already in progress.");
            }

            // Currency and amount are read back from OUR row, never Stripe's echo: that row is what
            // S5 asserts against, so describing the charge any other way describes a different one.
            return CheckoutSessionDto.From(
                live.SessionId, liveUrl, live.ExpiresAt, live.Currency, live.AmountMinor);
        }

        // Neither open nor complete: expired, or an id Stripe does not recognise (a key or account
        // swapped underneath us). Retire the row and let the caller mint a fresh session, rather
        // than handing the diner a dead page or wedging the order forever.
        live.Status = CheckoutSessionStatus.Expired;
        live.LastError = $"Stripe reported status '{remote?.Status ?? "unknown"}' when reusing.";
        await _context.SaveChangesAsync(cancellationToken);

        return null;
    }
}
