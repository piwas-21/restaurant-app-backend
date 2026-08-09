using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;

/// <summary>
/// The order id is the WHOLE request. Nothing about the money is accepted from the caller — that is
/// the point of the slice, and the direct continuation of S0b: the server decided what this order
/// costs when it created it, and the charge is read back from that row.
/// </summary>
public record CreateCheckoutSessionCommand : ICommand<ApiResponse<CheckoutSessionDto>>
{
    public Guid OrderId { get; set; }
}

public class CreateCheckoutSessionCommandHandler
    : ICommandHandler<CreateCheckoutSessionCommand, ApiResponse<CheckoutSessionDto>>
{
    /// <summary>Stripe's documented minimum, chosen over the 24 h default so an abandoned redirect
    /// releases the order promptly once the reconciler (S7) exists.</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _context;
    private readonly IStripeGateway _gateway;
    private readonly IStripeCheckoutClient _checkout;
    private readonly ICurrentUserService _currentUser;
    private readonly LocalizationSettings _localization;
    private readonly ILogger<CreateCheckoutSessionCommandHandler> _logger;

    public CreateCheckoutSessionCommandHandler(
        ApplicationDbContext context,
        IStripeGateway gateway,
        IStripeCheckoutClient checkout,
        ICurrentUserService currentUser,
        IOptions<LocalizationSettings> localization,
        ILogger<CreateCheckoutSessionCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(localization);

        _context = context;
        _gateway = gateway;
        _checkout = checkout;
        _currentUser = currentUser;
        _localization = localization.Value;
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

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Order not found");

        OnlinePaymentEligibility.EnsurePayable(order);

        var amount = CheckoutAmount.From(order.Total, _localization.Currency);

        var existing = await _context.OrderCheckoutSessions
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var reused = await TryReuseAsync(existing, cancellationToken);
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
                CustomerEmail = order.CustomerEmail,
            },
            cancellationToken);

        // A session with no URL cannot be paid, and recording it anyway would leave a row the
        // reconciler (S7) must later cancel an order over, for a redirect that never happened.
        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new BadRequestException("Online payment could not be started. Please try again.");
        }

        _context.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = session.Id,
            Status = CheckoutSessionStatus.Created,
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

        _logger.LogInformation(
            "Checkout session {SessionId} created for order {OrderNumber} ({AmountMinor} {Currency})",
            session.Id, order.OrderNumber, amount.Minor, amount.Currency);

        return ApiResponse<CheckoutSessionDto>.SuccessWithData(
            CheckoutSessionDto.From(session.Id, session.Url, expiresAt, amount.Currency, amount.Minor));
    }

    /// <summary>
    /// Hands back the live session's page instead of minting a second one, so a double-click or a
    /// back-button retry cannot leave two payable sessions against one order.
    ///
    /// <para>
    /// Stripe is asked, not the local row: with no webhook (plan §4) our <c>Created</c> is only a
    /// claim about the past. A session Stripe reports as paid is left untouched — settling belongs
    /// to S5, and half-settling here would put a second writer on the one transition that must
    /// happen exactly once.
    /// </para>
    /// </summary>
    private async Task<CheckoutSessionDto?> TryReuseAsync(
        IReadOnlyCollection<OrderCheckoutSession> sessions, CancellationToken cancellationToken)
    {
        var live = sessions.FirstOrDefault(s => s.Status == CheckoutSessionStatus.Created);
        if (live is null) return null;

        var remote = await _checkout.GetAsync(live.SessionId, cancellationToken);

        if (remote?.IsPaid == true) throw new BadRequestException("This order has already been paid.");

        // Currency and amount come from OUR row, never Stripe's echo: that row is what S5 asserts
        // against, so describing the charge any other way would describe a different one.
        if (remote?.IsOpen == true)
        {
            return CheckoutSessionDto.From(live.SessionId, remote.Url!, live.ExpiresAt, live.Currency, live.AmountMinor);
        }

        // No longer open (expired, or unknown to Stripe). Record it and fall through to a fresh
        // session rather than handing the diner a dead page.
        live.Status = CheckoutSessionStatus.Expired;
        live.LastError = $"Stripe reported status '{remote?.Status ?? "unknown"}' when reusing.";
        await _context.SaveChangesAsync(cancellationToken);

        return null;
    }
}
