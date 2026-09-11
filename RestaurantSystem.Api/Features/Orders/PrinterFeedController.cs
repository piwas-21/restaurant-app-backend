using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;

namespace RestaurantSystem.Api.Features.Orders;

// Dedicated controller for the printer-app feed endpoint. Split out from
// OrdersController in Sprint 2 task 2.3 (god-class decomposition).
//
// Auth: X-Api-Key header via [ApiKeyAuthFilter] (per ADR-003 in the
// printer-app repo). No user context — the printer-app is a service
// account, not a user.
//
// Response shape: legacy printer-app contract — HTTP 200 always, with
// `success: false` and the error message in the body on failure. The
// printer-app branches on the body's `success` field, so 5xx responses
// would break it. The `data.updates` member is additive; legacy `items`,
// `totalCount`, `page`, and `pageSize` retain their existing meaning.
//
// ONE carve-out: [RequireModule] below answers 404 when the tenant has no
// `printing` module. That is deliberate and terminal, not a transient
// failure to retry — a tenant without the module has no printer-app
// deployed to receive it, and the alternative (a 200 with an empty feed)
// would look like a working printer that never prints.
[ApiController]
[RequireModule(ModuleIds.Printing)]
[Route("api/orders/printer-feed")]
public class PrinterFeedController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly ILogger<PrinterFeedController> _logger;

    public PrinterFeedController(CustomMediator mediator, ILogger<PrinterFeedController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Returns legacy order tickets and additive kitchen update jobs for the printer-app.
    /// <paramref name="modifiedSince"/> remains the initial update boundary; subsequent update
    /// pages use <paramref name="updateCursor"/>.
    /// </summary>
    [HttpGet]
    [ApiKeyAuthFilter]
    public async Task<ActionResult<object>> Get(
        [FromQuery] DateTime? modifiedSince,
        [FromQuery] string? language,
        [FromQuery] string? updateCursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var orderDtos = await _mediator.SendQuery(
                new PrinterFeedQuery(modifiedSince, language),
                cancellationToken);
            var updatePage = await _mediator.SendQuery(
                new PrinterFeedUpdatesQuery(modifiedSince, updateCursor), cancellationToken);

            return Ok(new
            {
                success = true,
                data = new
                {
                    items = orderDtos,
                    totalCount = orderDtos.Count,
                    page = 1,
                    pageSize = PrinterFeedQuery.MaxOrdersPerPoll,
                    updates = updatePage.Items,
                    nextUpdateCursor = updatePage.NextUpdateCursor,
                    hasMoreUpdates = updatePage.HasMoreUpdates
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in printer feed");
            return Ok(new
            {
                success = false,
                message = ex.Message,
                data = new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    updates = Array.Empty<object>(),
                    nextUpdateCursor = (string?)null,
                    hasMoreUpdates = false
                }
            });
        }
    }

}
