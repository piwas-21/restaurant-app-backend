using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class GuestOrderReceiptSender : IGuestOrderReceiptSender
{

    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly IOutboundEmailLedger _ledger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuestOrderReceiptSender> _logger;

    public GuestOrderReceiptSender(
        IEmailService emailService,
        IEmailLanguageResolver languages,
        IOutboundEmailLedger ledger,
        IServiceScopeFactory scopeFactory,
        ILogger<GuestOrderReceiptSender> logger)
    {
        _emailService = emailService;
        _languages = languages;
        _ledger = ledger;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            // GAP-13: the old fallback really did post to noemail@example.com. Now that the server
            // sends this mail unprompted, that would be a guaranteed hard bounce on every emailless
            // order, charged to the tenant's own sending reputation.
            _logger.LogInformation(
                "Order {OrderNumber} has no customer email; skipping the order-received mail",
                order.OrderNumber);
            return;
        }

        if (!await _ledger.TryClaimAsync(OutboundEmailTypes.OrderReceived, order.Id))
        {
            _logger.LogInformation(
                "Order-received mail for order {OrderNumber} is already sent or in flight; skipping",
                order.OrderNumber);
            return;
        }

        try
        {
            // The order's own frozen language, read off the DTO. Resolved HERE rather than before
            // the queue — unlike the admin alert — because this method is also the resend endpoint's
            // synchronous path, which has no queue to be before. That is safe only because ForGuest
            // is request-free BY CONSTRUCTION (it hard-codes requestLanguage: null and the resolver's
            // other state is process-fixed): this task's ExecutionContext still carries the queueing
            // request's HttpContext, so anything here that COULD read the request would mail the
            // guest in whatever language that request asked for (§6.10). Do not replace this call
            // with one that takes a request language.
            await _emailService.SendOrderReceivedEmailAsync(
                _languages.ForGuest(order.PreferredLanguage),
                order.CustomerEmail,
                order.CustomerName ?? string.Empty,
                new OrderMailDetails(
                    order.OrderNumber,
                    order.Type,
                    order.Total,
                    OrderEmailComposer.ComposeItems(order),
                    SpecialInstructions: order.Notes,
                    DeliveryAddress: OrderEmailComposer.ComposeDeliveryAddress(order)));

            await _ledger.MarkSentAsync(OutboundEmailTypes.OrderReceived, order.Id);
        }
        catch
        {
            // Give the claim back so this order's receipt stays sendable — by the client's legacy
            // call, or by a support-triggered resend.
            await _ledger.ReleaseAsync(OutboundEmailTypes.OrderReceived, order.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public void Queue(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var scopeFactory = _scopeFactory;
        var logger = _logger;
        var orderNumber = order.OrderNumber;

        _ = Task.Run(async () =>
        {
            try
            {
                // A fresh scope, resolved inside the task: the request scope this was queued from
                // is gone by the time a slow provider answers, and its IEmailService with it
                // (issue #13, the ObjectDisposedException that made the admin alert do the same).
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IGuestOrderReceiptSender>();
                await sender.SendAsync(order);
            }
            catch (Exception ex)
            {
                // Nobody is waiting: the order is committed and the guest has been told so. The
                // claim was already released by SendAsync, so the mail is still sendable.
                logger.LogError(ex, "Failed to send order-received email for order {OrderNumber}", orderNumber);
            }
        });
    }
}
