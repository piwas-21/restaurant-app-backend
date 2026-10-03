using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelAvailabilityQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoriesQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoryChangesQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueSelectionQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.DeliveryChannels;

[ApiController]
[Route("api/delivery-channels/catalogue")]
[Authorize(Policy = DeliveryChannelServiceExtensions.MachineCataloguePolicy)]
[ApiScope(ApiTokenScopes.ChannelCatalogueRead)]
public sealed class ChannelCatalogueController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("availability")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelAvailabilitySnapshot>> Availability(ChannelAvailabilityRequest request)
        => Ok(await mediator.SendQuery(new GetChannelAvailabilityQuery(request), HttpContext.RequestAborted));

    [HttpPost("snapshot")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelCatalogueSnapshot>> Snapshot(ChannelCatalogueRequest request)
        => Ok(await mediator.SendQuery(new GetChannelCatalogueQuery(request), HttpContext.RequestAborted));

    [HttpPost("categories/snapshot")]
    public async Task<ActionResult<ChannelCatalogueCategoriesSnapshot>> Categories(CancellationToken cancellationToken)
        => Ok(await mediator.SendQuery(new GetChannelCatalogueCategoriesQuery(), cancellationToken));

    [HttpPost("categories/compare-snapshot")]
    [RequestSizeLimit(ExternalOrderLimits.CategoryReferenceRequestBytes)]
    public async Task<ActionResult<ChannelCatalogueCategoriesSnapshot>> CompareCategories(
        ChannelCatalogueCategoryReferencesRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.SendQuery(new GetChannelCatalogueCategoryChangesQuery(request), cancellationToken));

    [HttpPost("selection-snapshot")]
    [RequestSizeLimit(ExternalOrderLimits.CategoryReferenceRequestBytes)]
    public async Task<ActionResult<ChannelCatalogueSelectionSnapshot>> Selection(ChannelCatalogueSelectionSnapshotRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.SendQuery(new GetChannelCatalogueSelectionQuery(request), cancellationToken));
}
