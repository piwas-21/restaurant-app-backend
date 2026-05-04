using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.ApproveDelayCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders;

// Email-link landing endpoints split out of OrdersController as part of the
// Sprint 2 god-class decomposition (task 2.5). Each action returns an HTML
// status page rendered via IHtmlResponseBuilder (task 2.1). All values
// from URL path parameters (orderNumber, id) and from
// CommandResult.Message are HTML-escaped before composition — fixes a
// pre-existing XSS pattern in the inline-HTML versions of these actions.
[ApiController]
[Route("api/orders")]
public class OrderQuickActionsController : ControllerBase
{
    // Orders with prep time above this threshold need explicit customer
    // approval before transitioning to Confirmed; below this they
    // auto-confirm. Promote to config (e.g. OrderSettings:DelayThresholdMinutes)
    // if it ever needs to vary per deployment.
    private const int DelayThresholdMinutes = 10;
    private const int ConfirmRedirectSeconds = 5;
    private const int CancelRedirectSeconds = 3;

    private readonly CustomMediator _mediator;
    private readonly IHtmlResponseBuilder _html;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<OrderQuickActionsController> _logger;

    public OrderQuickActionsController(
        CustomMediator mediator,
        IHtmlResponseBuilder html,
        IOptions<EmailSettings> emailSettings,
        ILogger<OrderQuickActionsController> logger)
    {
        _mediator = mediator;
        _html = html;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <summary>Quick confirm order from email link.</summary>
    [HttpGet("{orderNumber}/quick-confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> QuickConfirmOrder(string orderNumber, [FromQuery] int minutes = 15)
    {
        try
        {
            var order = await FindOrderByNumber(orderNumber);
            if (order == null) return HtmlNotFound(orderNumber);

            if (order.Status != "Pending")
            {
                return Html(new HtmlStatusPage
                {
                    Title = "Order Already Processed",
                    Icon = "ℹ️",
                    AccentColor = "#374151",
                    Heading = "Order Already Processed",
                    Style = HtmlPageStyle.Plain,
                    BodyHtml = $"<p>Order {_html.Escape(orderNumber)} has already been {_html.Escape(order.Status.ToLower())}.</p>" +
                               $"<p>Current status: <strong>{_html.Escape(order.Status)}</strong></p>",
                });
            }

            var newStatus = minutes > DelayThresholdMinutes
                ? OrderStatus.PendingApproval
                : OrderStatus.Confirmed;

            var statusNote = minutes > DelayThresholdMinutes
                ? $"Pending customer approval for {minutes} min preparation time"
                : $"Confirmed via email with {minutes} min preparation time";

            var result = await _mediator.SendCommand(new UpdateOrderStatusCommand
            {
                OrderId = order.Id,
                NewStatus = newStatus,
                EstimatedPreparationMinutes = minutes,
                Notes = statusNote,
            });

            if (!result.Success) return HtmlActionFailed("Confirmation Failed", result.Message);

            var redirect = new HtmlRedirect($"{_emailSettings.FrontendBaseUrl}/admin/orders-management", ConfirmRedirectSeconds);
            var safeOrderNumber = _html.Escape(orderNumber);
            return minutes > DelayThresholdMinutes
                ? Html(new HtmlStatusPage
                {
                    Title = "Pending Customer Approval",
                    Icon = "⏳",
                    AccentColor = "#f59e0b",
                    Heading = "Awaiting Customer Approval",
                    Style = HtmlPageStyle.Plain,
                    Redirect = redirect,
                    BodyHtml = $"<p>Order <strong>{safeOrderNumber}</strong> requires customer approval for the {minutes}-minute preparation time.</p>" +
                               "<p>The customer will receive an email to approve or reject this delay.</p>",
                })
                : Html(new HtmlStatusPage
                {
                    Title = "Order Confirmed",
                    Icon = "✓",
                    AccentColor = "#059669",
                    Heading = "Order Confirmed!",
                    Style = HtmlPageStyle.Plain,
                    Redirect = redirect,
                    BodyHtml = $"<p>Order <strong>{safeOrderNumber}</strong> has been confirmed.</p>" +
                               $"<p>Preparation time: <strong>{minutes} minutes</strong></p>",
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quick confirm order {OrderNumber}", orderNumber);
            return HtmlGenericError("An error occurred while confirming the order.");
        }
    }

    /// <summary>Quick cancel order from email link.</summary>
    [HttpGet("{orderNumber}/quick-cancel")]
    [AllowAnonymous]
    public async Task<IActionResult> QuickCancelOrder(string orderNumber)
    {
        try
        {
            var order = await FindOrderByNumber(orderNumber);
            if (order == null) return HtmlNotFound(orderNumber);

            if (order.Status == "Cancelled")
            {
                return Html(new HtmlStatusPage
                {
                    Title = "Order Already Cancelled",
                    Icon = "ℹ️",
                    AccentColor = "#374151",
                    Heading = "Order Already Cancelled",
                    Style = HtmlPageStyle.Plain,
                    BodyHtml = $"<p>Order {_html.Escape(orderNumber)} has already been cancelled.</p>",
                });
            }

            if (order.Status != "Pending")
            {
                return Html(new HtmlStatusPage
                {
                    Title = "Cannot Cancel Order",
                    Icon = "⚠️",
                    AccentColor = "#f59e0b",
                    Heading = "Cannot Cancel Order",
                    Style = HtmlPageStyle.Plain,
                    BodyHtml = $"<p>Order {_html.Escape(orderNumber)} cannot be cancelled because it is already {_html.Escape(order.Status.ToLower())}.</p>" +
                               $"<p>Current status: <strong>{_html.Escape(order.Status)}</strong></p>",
                });
            }

            var result = await _mediator.SendCommand(new CancelOrderCommand
            {
                OrderId = order.Id,
                CancellationReason = "Cancelled by admin via email",
            });

            if (!result.Success) return HtmlActionFailed("Cancellation Failed", result.Message);

            return Html(new HtmlStatusPage
            {
                Title = "Order Cancelled",
                Icon = "✕",
                AccentColor = "#dc2626",
                Heading = "Order Cancelled",
                Style = HtmlPageStyle.Plain,
                Redirect = new HtmlRedirect($"{_emailSettings.FrontendBaseUrl}/admin/orders-management", CancelRedirectSeconds),
                BodyHtml = $"<p>Order <strong>{_html.Escape(orderNumber)}</strong> has been cancelled.</p>" +
                           "<p>The customer will be notified about the cancellation.</p>",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quick cancel order {OrderNumber}", orderNumber);
            return HtmlGenericError("An unexpected error occurred.");
        }
    }

    /// <summary>Customer approves a delayed-prep order from email link.</summary>
    [HttpGet("{id}/approve-delay")]
    [AllowAnonymous]
    public async Task<IActionResult> ApproveDelay(Guid id)
    {
        try
        {
            var result = await _mediator.SendCommand(new ApproveDelayCommand(id));
            return result.Success
                ? Html(new HtmlStatusPage
                {
                    Title = "Delay Approved",
                    Icon = "✓",
                    AccentColor = "#10b981",
                    Heading = "Delay Approved",
                    Style = HtmlPageStyle.Card,
                    BodyHtml = "<p>Thank you! Your order has been confirmed with the new preparation time.</p>" +
                               "<p>We're getting started on your delicious meal right away!</p>",
                })
                : HtmlActionFailedCard(result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve delay for order {OrderId}", id);
            return Content("An error occurred while processing your request.", "text/plain");
        }
    }

    /// <summary>Customer rejects a delayed-prep order (and cancels it) from email link.</summary>
    [HttpGet("{id}/reject-delay")]
    [AllowAnonymous]
    public async Task<IActionResult> RejectDelay(Guid id)
    {
        try
        {
            var result = await _mediator.SendCommand(new RejectDelayCommand(id));
            return result.Success
                ? Html(new HtmlStatusPage
                {
                    Title = "Order Cancelled",
                    Icon = "✕",
                    AccentColor = "#ef4444",
                    Heading = "Order Cancelled",
                    Style = HtmlPageStyle.Card,
                    BodyHtml = "<p>We've received your request to cancel the order.</p>" +
                               "<p>You will not be charged for this order.</p>" +
                               "<p>We hope to serve you again in the future!</p>",
                })
                : HtmlActionFailedCard(result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject delay for order {OrderId}", id);
            return Content("An error occurred while processing your request.", "text/plain");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<Dtos.OrderDto?> FindOrderByNumber(string orderNumber)
    {
        var orders = await _mediator.SendQuery(new GetOrdersQuery(
            Status: null, PaymentStatus: null, OrderType: null,
            StartDate: null, EndDate: null, UserId: null,
            Search: orderNumber, IsFocusOrder: null,
            OrderBy: "OrderDate", Descending: true,
            Page: 1, PageSize: 1));
        return orders.Data?.Items.FirstOrDefault();
    }

    private ContentResult Html(HtmlStatusPage page) =>
        Content(_html.BuildStatusPage(page), "text/html");

    private ContentResult HtmlNotFound(string orderNumber) => Html(new HtmlStatusPage
    {
        Title = "Order Not Found",
        Icon = "❌",
        AccentColor = "#dc2626",
        Heading = "Order Not Found",
        Style = HtmlPageStyle.Plain,
        BodyHtml = $"<p>Order {_html.Escape(orderNumber)} could not be found.</p>",
    });

    private ContentResult HtmlActionFailed(string title, string? message) => Html(new HtmlStatusPage
    {
        Title = title,
        Icon = "❌",
        AccentColor = "#dc2626",
        Heading = title,
        Style = HtmlPageStyle.Plain,
        BodyHtml = $"<p>{_html.Escape(message ?? "Action failed.")}</p>",
    });

    private ContentResult HtmlActionFailedCard(string? message) => Html(new HtmlStatusPage
    {
        Title = "Action Failed",
        Icon = "❌",
        AccentColor = "#ef4444",
        Heading = "Action Failed",
        Style = HtmlPageStyle.Card,
        BodyHtml = $"<p>{_html.Escape(message ?? "Action failed.")}</p>",
    });

    private ContentResult HtmlGenericError(string message) => Html(new HtmlStatusPage
    {
        Title = "Error",
        Icon = "❌",
        AccentColor = "#dc2626",
        Heading = "Error",
        Style = HtmlPageStyle.Plain,
        BodyHtml = $"<p>{_html.Escape(message)}</p>",
    });
}
