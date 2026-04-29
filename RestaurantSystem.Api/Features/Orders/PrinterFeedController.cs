using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

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
// would break it. Do not change without coordinating with the printer-app
// repo.
[ApiController]
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
    /// Returns confirmed orders for the printer-app to print.
    /// </summary>
    [HttpGet]
    [ApiKeyAuthFilter]
    public async Task<ActionResult<object>> Get([FromQuery] DateTime? modifiedSince)
    {
        try
        {
            var orderDtos = await _mediator.SendQuery(new PrinterFeedQuery(modifiedSince));

            return Ok(new
            {
                success = true,
                data = new
                {
                    items = orderDtos,
                    totalCount = orderDtos.Count,
                    page = 1,
                    pageSize = 50
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
                data = new { items = Array.Empty<object>(), totalCount = 0 }
            });
        }
    }
}
