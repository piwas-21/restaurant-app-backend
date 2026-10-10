using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;
using RestaurantSystem.Api.Features.Orders.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;

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
    private readonly OrderRoutingSettings _routingSettings;
    private readonly TimeProvider _timeProvider;

    public PrinterFeedController(
        CustomMediator mediator,
        ILogger<PrinterFeedController> logger,
        IOptions<OrderRoutingSettings> routingSettings,
        TimeProvider timeProvider)
    {
        _mediator = mediator;
        _logger = logger;
        _routingSettings = routingSettings.Value;
        _timeProvider = timeProvider;
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
        [FromQuery] string? orderCursor,
        [FromQuery] int? projectionVersion,
        [ModelBinder(Name = "X-Device-Id", BinderType = typeof(OptionalDeviceHeaderModelBinder))]
        OptionalDeviceHeader deviceHeader,
        CancellationToken cancellationToken)
    {
        var requestedProjectionVersion = projectionVersion ?? 1;
        if (requestedProjectionVersion is not (1 or 2))
            throw new RestaurantSystem.Api.Common.Exceptions.BadRequestException(
                "Printer projectionVersion must be 1 or 2.");

        try
        {
            var deviceId = deviceHeader.IsPresent
                ? deviceHeader.Value ?? string.Empty
                : null;
            var recoveryFence = PrinterFeedOrderCursor.ResolveRecoveryFence(
                orderCursor, _timeProvider.GetUtcNow().UtcDateTime, _routingSettings);
            var orderDtos = await _mediator.SendQuery(
                new PrinterFeedQuery(
                    modifiedSince, language, deviceId, orderCursor, recoveryFence.CutoffUtc,
                    requestedProjectionVersion),
                cancellationToken);
            var updatePage = await _mediator.SendQuery(
                new PrinterFeedUpdatesQuery(modifiedSince, updateCursor), cancellationToken);

            if (requestedProjectionVersion == 2)
                PrinterFeedV2Projection.NormalizeUpdates(updatePage.Items);

            return Ok(new
            {
                success = true,
                data = PrinterFeedResponseBuilder.BuildData(
                    orderDtos,
                    orderDtos.Count == PrinterFeedQuery.MaxOrdersPerPoll,
                    orderDtos.Count > 0
                        ? PrinterFeedOrderCursor.Encode(
                            orderDtos[^1], recoveryFence.CutoffUtc, recoveryFence.IssuedAtUtc)
                        : null,
                    updatePage,
                    requestedProjectionVersion)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in printer feed");
            return Ok(new
            {
                success = false,
                message = "Printer feed request failed. Retry shortly.",
                data = PrinterFeedResponseBuilder.BuildErrorData(projectionVersion)
            });
        }
    }
}
