using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;

/// <summary>
/// The order id is the WHOLE request. Nothing about the money is accepted from the caller — that is
/// the point of the slice, and the direct continuation of S0b: the server decided what this order
/// costs when it created it, and the charge is read back from that row.
/// </summary>
public record CreateCheckoutSessionCommand : ICommand<ApiResponse<CheckoutSessionDto>>
{
    /// <summary>
    /// <c>[JsonRequired]</c> for the reason the codebase already applies it elsewhere
    /// (<c>CreateOrderFromBasketCommand</c>, <c>SetBasketOrderTypeCommand</c>): a non-nullable
    /// value type binds an OMITTED field to <c>Guid.Empty</c>, so under-posting would arrive here
    /// as a well-formed request for an order that cannot exist. This makes the omission a 400 at
    /// model binding; the validator's <c>NotEmpty</c> still covers an all-zeros id sent on purpose.
    /// </summary>
    [JsonRequired]
    public Guid OrderId { get; set; }
}

public class CreateCheckoutSessionCommandHandler
    : ICommandHandler<CreateCheckoutSessionCommand, ApiResponse<CheckoutSessionDto>>
{
    /// <summary>
    /// Stripe's documented minimum is 30 minutes, chosen over the 24 h default so an abandoned
    /// redirect releases the order promptly once the reconciler (S7) exists.
    /// </summary>
    /// <remarks>
    /// The extra minute is not padding. <c>expires_at</c> must be at least 30 minutes ahead when
    /// STRIPE evaluates it, and this timestamp is stamped before the HTTPS round trip — so an exact
    /// 30:00 arrives as 29:59 under ordinary latency or a little forward clock skew and the API
    /// rejects the whole session. That failure is intermittent and environment-dependent, i.e. the
    /// kind no test with a faked client can catch.
    /// </remarks>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(31);

    private readonly ApplicationDbContext _context;
    private readonly IStripeGateway _gateway;
    private readonly IStripeCheckoutClient _checkout;
    private readonly ICheckoutSessionReuse _reuse;
    private readonly ICurrentUserService _currentUser;
    private readonly ICheckoutChargeResolver _charge;
    private readonly ILogger<CreateCheckoutSessionCommandHandler> _logger;

    public CreateCheckoutSessionCommandHandler(
        ApplicationDbContext context,
        IStripeGateway gateway,
        IStripeCheckoutClient checkout,
        ICheckoutSessionReuse reuse,
        ICurrentUserService currentUser,
        ICheckoutChargeResolver charge,
        ILogger<CreateCheckoutSessionCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(charge);

        _context = context;
        _gateway = gateway;
        _checkout = checkout;
        _reuse = reuse;
        _currentUser = currentUser;
        _charge = charge;
        _logger = logger;
    }

    public async Task<ApiResponse<CheckoutSessionDto>> Handle(
        CreateCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The controller's module gate says the tenant BOUGHT online payments; this says it can
        // actually transact. Neither implies the other — a bought module with no connected account
        // is exactly the state between signup and Stripe onboarding.
        if (!_gateway.IsConfigured)
        {
            throw new BadRequestException("Online payment is not available for this restaurant.");
        }

        // AsNoTracking: the order is read for its price and its status and is never written here.
        // Online payment does not touch the order — the tender, the deferred confirm and the
        // fidelity award are all S5's, on the settle path.
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Order not found");

        OnlinePaymentEligibility.EnsurePayable(order);

        // One call for both numbers: the amount is still the PERSISTED order total and nothing
        // else, and the fee is a share of that same amount. See ICheckoutChargeResolver.
        var (amount, applicationFeeMinor) = _charge.Resolve(order.Total);

        var existing = await _context.OrderCheckoutSessions
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var reused = await _reuse.TryReuseAsync(existing, amount, cancellationToken);
        if (reused is not null) return ApiResponse<CheckoutSessionDto>.SuccessWithData(reused);

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SessionLifetime);

        // `attempt` counts every session ever minted for this order, expired ones included, and is
        // DERIVED rather than random on purpose. That makes two concurrent callers compute the same
        // key, so Stripe replays one session to both and the unique index on SessionId rejects the
        // second insert — one order cannot end up with two payable sessions. (The loser sees a 500
        // and retries into the reuse path above, which is correct if ugly.)
        var idempotencyKey = $"checkout:{order.Id}:{existing.Count + 1}";

        var session = await _checkout.CreateAsync(
            new CheckoutSessionRequest
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Currency = amount.Currency,
                AmountMinor = amount.Minor,
                ExpiresAt = expiresAt,
                IdempotencyKey = idempotencyKey,
                ApplicationFeeMinor = applicationFeeMinor,
            },
            cancellationToken);

        // A session with no URL cannot be paid. It is still RECORDED — as Failed, not Created — for
        // two reasons: the session exists on the connected account whether or not we like it, and
        // the row is what advances the attempt counter. Throwing without recording would replay the
        // same idempotency key forever, and Stripe would hand back the same unusable session on
        // every retry: the order could never be paid. Failed is terminal, so the reconciler (S7)
        // will not cancel an order over it either.
        var usable = !string.IsNullOrWhiteSpace(session.Url);

        _context.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = session.Id,
            Status = usable ? CheckoutSessionStatus.Created : CheckoutSessionStatus.Failed,
            LastError = usable ? null : "Stripe returned a session with no hosted-page URL.",
            Currency = amount.Currency,
            AmountMinor = amount.Minor,
            IdempotencyKey = idempotencyKey,
            ExpiresAt = expiresAt,
            ConnectedAccountId = _gateway.ConnectedAccountId,
            CreatedAt = now,
            // "System" for the guest checkout this mostly serves (ADR-004 — no account to name),
            // the user id when a signed-in customer pays. §5.13: never an inline ternary.
            CreatedBy = _currentUser.GetAuditIdentifier(),
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new BadRequestException("Online payment could not be started. Please try again.");
        }

        _logger.LogInformation(
            "Checkout session {SessionId} created for order {OrderNumber} ({AmountMinor} {Currency})",
            session.Id, order.OrderNumber, amount.Minor, amount.Currency);

        return ApiResponse<CheckoutSessionDto>.SuccessWithData(
            CheckoutSessionDto.From(session.Id, session.Url, expiresAt, amount.Currency, amount.Minor));
    }
}
