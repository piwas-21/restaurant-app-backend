using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCandidatesQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Management;

[ApiController]
[Route("api/delivery-channels/management/uber")]
[Authorize(Policy = DeliveryChannelServiceExtensions.HumanManagementPolicy)]
public sealed class DeliveryChannelManagementController(
    CustomMediator mediator,
    IDeliveryChannelManagementClient gateway,
    ICurrentUserService currentUser,
    IOptions<DeliveryChannelManagementSettings> managementSettings,
    IOptions<DeliveryChannelSettings> channelSettings) : ControllerBase
{
    [HttpGet]
    public Task<DeliveryChannelManagementSummaryDto> Summary(CancellationToken cancellationToken)
        => Read<DeliveryChannelManagementSummaryDto>("summary", cancellationToken);

    [HttpPost("oauth/start")]
    [RequestSizeLimit(4096)]
    public Task<DeliveryChannelOAuthStartDto> StartOAuth(DeliveryChannelOAuthStartRequest request,
        CancellationToken cancellationToken)
        => Write<DeliveryChannelOAuthStartDto>("oauth/start", request, cancellationToken);

    [HttpGet("oauth/flows/{flowId:guid}")]
    public Task<DeliveryChannelOAuthFlowDto> OAuthFlow(Guid flowId, CancellationToken cancellationToken)
        => Read<DeliveryChannelOAuthFlowDto>($"oauth/flows/{flowId:D}", cancellationToken);

    [HttpGet("catalogue")]
    public Task<DeliveryChannelCatalogueDto> Catalogue(CancellationToken cancellationToken)
        => Read<DeliveryChannelCatalogueDto>("catalogue", cancellationToken);

    [HttpGet("catalogue/candidates")]
    public async Task<ActionResult<ChannelCatalogueCandidatesDto>> Candidates(
        [FromQuery] string search = "", [FromQuery] string cursor = "", CancellationToken cancellationToken = default)
    {
        RequireEnabled();
        Response.Headers.CacheControl = "no-store";
        var result = await mediator.SendQuery(new GetChannelCatalogueCandidatesQuery(search, cursor, "en"), cancellationToken);
        return Ok(result);
    }

    [HttpPut("catalogue/draft")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public Task<DeliveryChannelCatalogueDraftDto> SaveDraft(DeliveryChannelCatalogueDraftRequest request,
        CancellationToken cancellationToken)
        => Put<DeliveryChannelCatalogueDraftDto>("catalogue/draft", request, cancellationToken);

    [HttpPost("catalogue/preview")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public Task<DeliveryChannelPreviewDto> Preview(DeliveryChannelPreviewRequest request,
        CancellationToken cancellationToken)
        => Write<DeliveryChannelPreviewDto>("catalogue/preview", request, cancellationToken);

    [HttpPost("catalogue/publish")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public Task<DeliveryChannelPublicationDto> Publish(DeliveryChannelPublishRequest request,
        CancellationToken cancellationToken)
        => Write<DeliveryChannelPublicationDto>("catalogue/publish", request, cancellationToken);

    [HttpGet("catalogue/publications/{publicationId:guid}")]
    public Task<DeliveryChannelPublicationDto> Publication(Guid publicationId, CancellationToken cancellationToken)
        => Read<DeliveryChannelPublicationDto>($"catalogue/publications/{publicationId:D}", cancellationToken);

    [HttpGet("availability")]
    public Task<DeliveryChannelAvailabilityDto> Availability(CancellationToken cancellationToken)
        => Read<DeliveryChannelAvailabilityDto>("availability", cancellationToken);

    [HttpPost("availability/pause")]
    [RequestSizeLimit(4096)]
    public Task<DeliveryChannelAvailabilityActionDto> Pause(DeliveryChannelPauseRequest request,
        CancellationToken cancellationToken)
        => Write<DeliveryChannelAvailabilityActionDto>("availability/pause", request, cancellationToken);

    [HttpPost("availability/resume")]
    [RequestSizeLimit(4096)]
    public Task<DeliveryChannelAvailabilityActionDto> Resume(CancellationToken cancellationToken)
        => Write<DeliveryChannelAvailabilityActionDto>("availability/resume", null, cancellationToken);

    [HttpGet("exceptions")]
    public Task<DeliveryChannelExceptionInboxDto> Exceptions([FromQuery] string cursor = "",
        CancellationToken cancellationToken = default)
        => Read<DeliveryChannelExceptionInboxDto>(QueryHelpers.AddQueryString("exceptions", "cursor", cursor), cancellationToken);

    [HttpGet("exceptions/{exceptionId:guid}")]
    public Task<DeliveryChannelExceptionDto> Exception(Guid exceptionId, CancellationToken cancellationToken)
        => Read<DeliveryChannelExceptionDto>($"exceptions/{exceptionId:D}", cancellationToken);

    [HttpPost("exceptions/{exceptionId:guid}/reconcile")]
    [RequestSizeLimit(4096)]
    public Task<DeliveryChannelReconcileResultDto> Reconcile(Guid exceptionId, CancellationToken cancellationToken)
        => Write<DeliveryChannelReconcileResultDto>($"exceptions/{exceptionId:D}/reconcile", null, cancellationToken);

    [HttpPost("disconnect")]
    [RequestSizeLimit(4096)]
    public Task<DeliveryChannelDisconnectResultDto> Disconnect(DeliveryChannelDisconnectRequest request,
        CancellationToken cancellationToken)
        => Write<DeliveryChannelDisconnectResultDto>("disconnect", request, cancellationToken);

    private Task<TResponse> Read<TResponse>(string path, CancellationToken cancellationToken)
        => Send<TResponse>(HttpMethod.Get, path, null, cancellationToken);

    private Task<TResponse> Write<TResponse>(string path, object? body, CancellationToken cancellationToken)
        => Send<TResponse>(HttpMethod.Post, path, body, cancellationToken);

    private Task<TResponse> Put<TResponse>(string path, object? body, CancellationToken cancellationToken)
        => Send<TResponse>(HttpMethod.Put, path, body, cancellationToken);

    private Task<TResponse> Send<TResponse>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var actorId = currentUser.UserId ?? throw new ForbiddenException("A signed-in tenant administrator is required.");
        Response.Headers.CacheControl = "no-store";
        return gateway.Send<TResponse>(method, $"api/tenant-management/uber/{path}", actorId, body, cancellationToken);
    }

    private void RequireEnabled()
    {
        if (!managementSettings.Value.Enabled || !channelSettings.Value.Enabled)
            throw new NotFoundException("Delivery channel management is not enabled for this tenant.", "ModuleNotEnabled");
    }
}
