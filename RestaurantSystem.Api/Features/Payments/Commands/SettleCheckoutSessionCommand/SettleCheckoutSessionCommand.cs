using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;

/// <summary>
/// Settles one Stripe hosted-Checkout session. Idempotent, and deliberately so: there is no webhook
/// in v1 (plan §4, measured — the platform may not register one on a connected account), so this
/// has TWO callers that can arrive in either order or at the same time — the diner's
/// <c>success_url</c> return trip (S9) and the polling reconciler (S7).
/// </summary>
public record SettleCheckoutSessionCommand : ICommand<ApiResponse<CheckoutSettlementDto>>
{
    /// <summary>Stripe's <c>cs_...</c>. The only input — everything else is re-read from Stripe.</summary>
    public required string SessionId { get; init; }
}

public class SettleCheckoutSessionCommandHandler
    : ICommandHandler<SettleCheckoutSessionCommand, ApiResponse<CheckoutSettlementDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IStripeCheckoutClient _checkout;
    private readonly ICheckoutSettlementWriter _writer;
    private readonly ILogger<SettleCheckoutSessionCommandHandler> _logger;

    public SettleCheckoutSessionCommandHandler(
        ApplicationDbContext context,
        IStripeCheckoutClient checkout,
        ICheckoutSettlementWriter writer,
        ILogger<SettleCheckoutSessionCommandHandler> logger)
    {
        _context = context;
        _checkout = checkout;
        _writer = writer;
        _logger = logger;
    }

    public async Task<ApiResponse<CheckoutSettlementDto>> Handle(
        SettleCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // AsNoTracking throughout. The claim inside the writer is an ExecuteUpdate, which does not
        // refresh tracked entities — a tracked copy of this row would silently go stale the moment
        // it lands, and the stalest field would be the one deciding whether to settle again.
        var session = await _context.OrderCheckoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, cancellationToken)
            ?? throw new NotFoundException("Checkout session not found");

        // Already terminal. Report, do not re-run: for Completed the tender exists, and for
        // Expired/Failed re-settling would resurrect a session we already gave up on.
        if (session.Status != CheckoutSessionStatus.Created)
        {
            return await DescribeAsync(session, cancellationToken);
        }

        // Stripe is the only authority. Our row records what we ASKED for; whether the diner paid
        // is knowable in exactly one place, and with no webhook this fetch is how we learn it.
        var remote = await _checkout.GetAsync(session.SessionId, cancellationToken);

        if (remote is null)
        {
            // Only ever `resource_missing` — GetAsync narrows to that and rethrows everything else,
            // so a 401 from a revoked key never lands here pretending the session did not exist.
            // The id is unusable by this key: a live/test swap, or a database restored across
            // environments. Retire it so the order is not wedged behind a session we cannot read.
            return await RetireAsync(
                session, CheckoutSessionStatus.Failed,
                "Stripe does not recognise this session id for the configured account.", cancellationToken);
        }

        if (!remote.IsComplete)
        {
            return await HandleIncompleteAsync(session, remote, cancellationToken);
        }

        // The amount assertion, and the reason the row stores AmountMinor at all. Stripe is the
        // authority on whether money moved, but our row is the authority on how much was supposed
        // to move; a disagreement means the two describe different charges and no tender may be
        // written from either. Refusing leaves the money visible in Stripe's dashboard for a human.
        if (remote.AmountTotalMinor != session.AmountMinor)
        {
            _logger.LogError(
                "Checkout session {SessionId} settled for {Actual} but expected {Expected} {Currency}",
                session.SessionId, remote.AmountTotalMinor, session.AmountMinor, session.Currency);

            return await RetireAsync(
                session, CheckoutSessionStatus.Failed,
                $"Stripe reported {remote.AmountTotalMinor} but this order recorded {session.AmountMinor}.",
                cancellationToken);
        }

        var settled = await _writer.SettleAsync(
            session, remote.PaymentIntentId, remote.AmountTotalMinor, cancellationToken);

        return ApiResponse<CheckoutSettlementDto>.SuccessWithData(settled);
    }

    /// <summary>
    /// Checkout has not completed. Either it is still payable, or it is over.
    /// </summary>
    /// <remarks>
    /// <c>complete</c> is terminal INDEPENDENTLY of <c>payment_status</c>, which is why the caller
    /// tests <c>IsComplete</c> and not <c>IsPaid</c>. A delayed-notification method — SEPA, Klarna,
    /// Sofort, all reachable precisely because payment methods are chosen dynamically — completes
    /// with <c>payment_status: unpaid</c> while the funds clear. Reading that as a failure would
    /// expire a session the diner has already been through, and mint a second one for the same
    /// money.
    ///
    /// <para>
    /// Only an explicit <c>expired</c> retires the row. Anything else — <c>open</c>, or a status
    /// Stripe adds later that this code has never seen — is left alone for the next sweep, because
    /// the row expiring is what eventually lets S7 CANCEL the order. Guessing in that direction
    /// destroys a live order; guessing the other way costs one more poll.
    /// </para>
    /// </remarks>
    private async Task<ApiResponse<CheckoutSettlementDto>> HandleIncompleteAsync(
        OrderCheckoutSession session, StripeCheckoutSession remote, CancellationToken cancellationToken)
    {
        if (!remote.IsExpired)
        {
            return await DescribeAsync(session, cancellationToken);
        }

        return await RetireAsync(
            session, CheckoutSessionStatus.Expired,
            "Stripe reported the session expired before it was paid.", cancellationToken);
    }

    /// <summary>
    /// Moves the row to a terminal status, but only if it is still <c>Created</c>.
    /// </summary>
    /// <remarks>
    /// The same conditional-UPDATE shape as the settle claim, and for the same reason: a settle
    /// running concurrently must win. Without the condition, a reconciler sweep that read "expired"
    /// a moment before the return trip settled could overwrite a Completed row — losing the only
    /// local record of a payment that Stripe has already taken.
    /// </remarks>
    private async Task<ApiResponse<CheckoutSettlementDto>> RetireAsync(
        OrderCheckoutSession session,
        CheckoutSessionStatus status,
        string reason,
        CancellationToken cancellationToken)
    {
        await _context.OrderCheckoutSessions
            .Where(s => s.Id == session.Id && s.Status == CheckoutSessionStatus.Created)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, status).SetProperty(x => x.LastError, reason),
                cancellationToken);

        _logger.LogWarning(
            "Checkout session {SessionId} retired as {Status}: {Reason}", session.SessionId, status, reason);

        return await DescribeAsync(session, cancellationToken);
    }

    /// <summary>Reports the order's current state without changing anything.</summary>
    private async Task<ApiResponse<CheckoutSettlementDto>> DescribeAsync(
        OrderCheckoutSession session, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstAsync(o => o.Id == session.OrderId, cancellationToken);

        return ApiResponse<CheckoutSettlementDto>.SuccessWithData(CheckoutSettlementDto.From(order));
    }
}
