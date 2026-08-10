using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class CheckoutSessionRetirement : ICheckoutSessionRetirement
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CheckoutSessionRetirement> _logger;

    public CheckoutSessionRetirement(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ILogger<CheckoutSessionRetirement> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RetireAsync(
        OrderCheckoutSession session,
        CheckoutSessionStatus status,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        var retired = await _context.OrderCheckoutSessions
            .Where(s => s.Id == session.Id && s.Status == CheckoutSessionStatus.Created)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.LastError, reason)
                    // ExecuteUpdate never goes through the change tracker, so ApplicationDbContext's
                    // IAuditable stamper does not run — these have to be set by hand or the one
                    // table whose job is being the claim ticket for money records no history of it.
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);

        if (retired == 0)
        {
            // Something else moved the row first — almost certainly a settle that got there before
            // this sweep. Leave the tender alone: it belongs to whatever won.
            _logger.LogInformation(
                "Checkout session {SessionId} was no longer claimable when retiring as {Status}",
                session.SessionId, status);
            return;
        }

        await FailTenderAsync(session, now, auditId, cancellationToken);

        _logger.LogWarning(
            "Checkout session {SessionId} retired as {Status}: {Reason}", session.SessionId, status, reason);
    }

    /// <summary>
    /// Releases the <c>Processing</c> tender the retired session was covering.
    /// </summary>
    /// <remarks>
    /// Without this the tender is unresolvable: <c>AddPaymentToOrder</c> sweeps only <c>Pending</c>
    /// tenders, refunds refuse anything that is not <c>Completed</c>, and settling is the sole other
    /// path that touches it. <c>UpdateOrderStatusCommand</c> then sees an order still awaiting an
    /// online payment that can never arrive.
    ///
    /// <para>
    /// <c>Failed</c> rather than deleting the row. A diner did start a payment, and an abandoned
    /// attempt is a fact worth keeping — it is also not <c>IsCaptured()</c>, so it stays out of every
    /// money total exactly as <c>Processing</c> did.
    /// </para>
    /// </remarks>
    private async Task FailTenderAsync(
        OrderCheckoutSession session, DateTime now, string auditId, CancellationToken cancellationToken)
    {
        // A second live session for the same order means the tender is still covering THAT one —
        // retiring this session says nothing about it. Rare (the mint path reuses a live session
        // rather than adding a second), but the cost of getting it wrong is voiding a tender for a
        // payment currently in progress.
        var anotherSessionIsLive = await _context.OrderCheckoutSessions.AnyAsync(
            s => s.OrderId == session.OrderId
                && s.Id != session.Id
                && s.Status == CheckoutSessionStatus.Created,
            cancellationToken);

        if (anotherSessionIsLive)
        {
            return;
        }

        await _context.OrderPayments
            .Where(p => p.OrderId == session.OrderId
                && p.PaymentMethod == PaymentMethod.OnlinePayment
                && p.Status == PaymentStatus.Processing)
            .ExecuteUpdateAsync(
                p => p
                    .SetProperty(x => x.Status, PaymentStatus.Failed)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);
    }
}
