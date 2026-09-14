using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;

/// <summary>
/// Settles one Stripe hosted-Checkout session idempotently for either caller: the diner's
/// <c>success_url</c> return trip or the polling reconciler used while no webhook is available.
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
    private readonly ICheckoutSessionRetirement _retirement;
    private readonly ILogger<SettleCheckoutSessionCommandHandler> _logger;
    private readonly int _maxAttempts;

    public SettleCheckoutSessionCommandHandler(
        ApplicationDbContext context,
        IStripeCheckoutClient checkout,
        ICheckoutSettlementWriter writer,
        ICheckoutSessionRetirement retirement,
        ILogger<SettleCheckoutSessionCommandHandler> logger,
        IOptions<CheckoutReconciliationSettings>? settings = null)
    {
        _context = context;
        _checkout = checkout;
        _writer = writer;
        _retirement = retirement;
        _logger = logger;
        _maxAttempts = (settings?.Value ?? new CheckoutReconciliationSettings()).SettlementMaxAttempts;
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
                "Checkout for order {OrderId} settled for {Actual} but expected {Expected} {Currency}",
                session.OrderId, remote.AmountTotalMinor, session.AmountMinor, session.Currency);

            return await RetireAsync(
                session, CheckoutSessionStatus.Failed,
                $"Stripe reported {remote.AmountTotalMinor} but this order recorded {session.AmountMinor}.",
                cancellationToken);
        }

        // A failed concurrency claim rolls back. Retry from a fresh snapshot up to the configured
        // bound; a persistent race returns a stable refusal from this idempotent endpoint.
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var settled = await _writer.SettleAsync(
                    session, remote.PaymentIntentId, remote.AmountTotalMinor, cancellationToken);
                return ApiResponse<CheckoutSettlementDto>.SuccessWithData(settled);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < _maxAttempts)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex,
                    "Checkout for order {OrderId} lost order version race; reloading for retry",
                    session.OrderId);
                session = await _context.OrderCheckoutSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == session.Id, cancellationToken)
                    ?? throw new NotFoundException("Checkout session not found");

                if (session.Status != CheckoutSessionStatus.Created)
                {
                    return await DescribeAsync(session, cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex,
                    "Checkout for order {OrderId} repeatedly lost the order version race",
                    session.OrderId);
                return ApiResponse<CheckoutSettlementDto>.FailureWithCode(
                    "The order changed while payment was settling. Please retry.",
                    ErrorCodes.OrderVersionConflict);
            }
        }

        return ApiResponse<CheckoutSettlementDto>.FailureWithCode(
            "The order changed while payment was settling. Please retry.",
            ErrorCodes.OrderVersionConflict);
    }

    private async Task<ApiResponse<CheckoutSettlementDto>> DescribeAsync(OrderCheckoutSession session, CancellationToken cancellationToken)
        => ApiResponse<CheckoutSettlementDto>.SuccessWithData(await _writer.DescribeAsync(session.OrderId, cancellationToken));

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
    /// Ends the session and releases the tender it was holding, then reports where that leaves the
    /// order. Delegated, because failing the tender is what stops an abandoned payment blocking the
    /// order forever — see <see cref="ICheckoutSessionRetirement"/>.
    /// </summary>
    private async Task<ApiResponse<CheckoutSettlementDto>> RetireAsync(
        OrderCheckoutSession session,
        CheckoutSessionStatus status,
        string reason,
        CancellationToken cancellationToken)
    {
        await _retirement.RetireAsync(session, status, reason, cancellationToken);

        return await DescribeAsync(session, cancellationToken);
    }

}
