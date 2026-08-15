using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class AdminOrderAlertSender : IAdminOrderAlertSender
{
    private const string FallbackCustomerEmail = "noemail@example.com";
    private const string FallbackPhone = "Not provided";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboundEmailLedger _ledger;
    private readonly IEmailLanguageResolver _languages;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<AdminOrderAlertSender> _logger;

    public AdminOrderAlertSender(
        IServiceScopeFactory scopeFactory,
        IOutboundEmailLedger ledger,
        IEmailLanguageResolver languages,
        IOptions<EmailSettings> emailSettings,
        ILogger<AdminOrderAlertSender> logger)
    {
        _scopeFactory = scopeFactory;
        _ledger = ledger;
        _languages = languages;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Queue(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Fire-and-forget. The original (pre-task-2.10) code captured the request-scoped
        // IEmailService inside Task.Run, which led to ObjectDisposedException when SMTP I/O
        // outlasted the response (issue #13). Fix: capture the IServiceScopeFactory (Singleton
        // lifetime) and resolve a fresh IEmailService inside the lambda's scope.
        var scopeFactory = _scopeFactory;
        var ledger = _ledger;
        var adminEmail = _emailSettings.AdminEmail;
        var logger = _logger;
        var items = OrderEmailComposer.ComposeItems(order);
        var deliveryAddress = OrderEmailComposer.ComposeDeliveryAddress(order);
        var orderNumber = order.OrderNumber;
        var orderId = order.Id;

        // The RESTAURANT's language, and resolved here rather than inside the task: this mail
        // follows the tenant, never the diner (§1 rank 4). Captured with the rest of the state the
        // lambda closes over, so the detached task reads no ambient anything.
        var culture = _languages.ForOperator();

        _ = Task.Run(async () =>
        {
            try
            {
                // Claimed inside the task rather than before it: a claim taken for a task that
                // never ran would suppress the restaurant's only email notice of the order for
                // good. Inside the try as well as inside the task — a database blip here would
                // otherwise fault a detached task nobody observes, losing even the log line.
                if (!await ledger.TryClaimAsync(OutboundEmailTypes.OrderAdminAlert, orderId))
                {
                    logger.LogInformation(
                        "Admin alert for order {OrderNumber} is already sent or in flight; skipping", orderNumber);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var quickActionToken = await ReadQuickActionTokenAsync(scope, orderId, orderNumber, logger);

                await emailService.SendOrderConfirmationAdminEmailAsync(
                    culture,
                    adminEmail,
                    orderNumber,
                    order.CustomerName ?? string.Empty,
                    order.CustomerEmail ?? FallbackCustomerEmail,
                    order.CustomerPhone ?? FallbackPhone,
                    order.Type,
                    order.Total,
                    items,
                    quickActionToken,
                    order.Notes,
                    deliveryAddress);

                await ledger.MarkSentAsync(OutboundEmailTypes.OrderAdminAlert, orderId);
            }
            catch (Exception ex)
            {
                // Release before logging: an un-released claim would make this alert permanently
                // unsendable, which is the failure mode GAP-11 is about.
                await ledger.ReleaseAsync(OutboundEmailTypes.OrderAdminAlert, orderId);
                logger.LogError(ex, "Failed to send admin notification email for order {OrderNumber}", orderNumber);
            }
        });
    }

    private static async Task<string?> ReadQuickActionTokenAsync(
        IServiceScope scope, Guid orderId, string orderNumber, ILogger logger)
    {
        // Read here rather than take it from OrderDto: the token authorises the anonymous
        // confirm/cancel endpoints (ORDER-TYPE-AVAILABILITY-PLAN §9.20), so putting it on the DTO
        // would publish a credential through every endpoint that returns an order. This scope is
        // the narrowest place that needs it.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var quickActionToken = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.QuickActionToken)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(quickActionToken))
        {
            // Not the documented legacy-row case: this path runs only for an order just committed
            // by CreateOrderCommandHandler, which mints the token at insert. So null here means the
            // row vanished, was soft-deleted, or the generator broke — and the owner silently
            // receives an email whose every button says "Order Not Found". Send it anyway (the
            // dashboard link still works), but say so.
            logger.LogWarning(
                "Order {OrderNumber} has no quick-action token; admin email will render dead confirm/cancel links",
                orderNumber);
        }

        return quickActionToken;
    }
}
