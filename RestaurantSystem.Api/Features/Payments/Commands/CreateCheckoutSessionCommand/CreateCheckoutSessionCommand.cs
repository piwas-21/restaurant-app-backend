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
    private readonly IOnlinePaymentIntentGuard _intentGuard;
    private readonly ICurrentUserService _currentUser;
    private readonly ICheckoutChargeResolver _charge;
    private readonly ILogger<CreateCheckoutSessionCommandHandler> _logger;

    public CreateCheckoutSessionCommandHandler(
        ApplicationDbContext context,
        IStripeGateway gateway,
        IStripeCheckoutClient checkout,
        ICheckoutSessionReuse reuse,
        IOnlinePaymentIntentGuard intentGuard,
        ICurrentUserService currentUser,
        ICheckoutChargeResolver charge,
        ILogger<CreateCheckoutSessionCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(charge);

        _context = context;
        _gateway = gateway;
        _checkout = checkout;
        _reuse = reuse;
        _intentGuard = intentGuard;
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
        // Track the order so adding a session bumps its aggregate version.
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Order not found");
        OnlinePaymentEligibility.EnsurePayable(order);
        await _intentGuard.EnsureProcessingAsync(order.Id, cancellationToken);
        // One call for both numbers: the amount is still the PERSISTED order total and nothing
        // else, and the fee is a share of that same amount. See ICheckoutChargeResolver.
        var (amount, applicationFeeMinor) = _charge.Resolve(order.Total);
        var existing = await _context.OrderCheckoutSessions
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var reused = await _reuse.TryReuseAsync(existing, amount, cancellationToken);
        if (reused is not null) return ApiResponse<CheckoutSessionDto>.SuccessWithData(reused);
        // Retirement uses set-based writes and may have bumped this tracked aggregate's version.
        // Reload before adding the replacement session so its aggregate touch cannot write stale state.
        await _context.Entry(order).ReloadAsync(cancellationToken);
        OnlinePaymentEligibility.EnsurePayable(order);
        await _intentGuard.ReactivateLatestFailedAsync(order.Id, cancellationToken);
        await _context.Entry(order).ReloadAsync(cancellationToken);
        OnlinePaymentEligibility.EnsurePayable(order);
        await _intentGuard.EnsureProcessingAsync(order.Id, cancellationToken);
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SessionLifetime);
        // A derived attempt key makes Stripe replay concurrent creates for the same order.
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

        // Record a URL-less session as Failed so its idempotency attempt is never replayed.
        var usable = !string.IsNullOrWhiteSpace(session.Url);

        try
        {
            await _context.Entry(order).ReloadAsync(cancellationToken);
            OnlinePaymentEligibility.EnsurePayable(order);
            await _intentGuard.EnsureProcessingAsync(order.Id, cancellationToken);
        }
        catch
        {
            await ExpireOrLogAsync(session.Id, order.Id, cancellationToken);
            throw;
        }
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

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await ExpireOrLogAsync(session.Id, order.Id, cancellationToken);
            throw;
        }

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new BadRequestException("Online payment could not be started. Please try again.");
        }

        _logger.LogInformation(
            "Checkout created for order {OrderId}/{OrderNumber} ({AmountMinor} {Currency})",
            order.Id, order.OrderNumber, amount.Minor, amount.Currency);

        return ApiResponse<CheckoutSessionDto>.SuccessWithData(
            CheckoutSessionDto.From(session.Id, session.Url, expiresAt, amount.Currency, amount.Minor));
    }

    private async Task ExpireOrLogAsync(
        string sessionId, Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            await _checkout.ExpireAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire orphaned Stripe checkout for order {OrderId}", orderId);
        }
    }
}
