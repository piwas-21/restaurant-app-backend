using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

[ApiController]
[Route("api/tenant-management/uber")]
[RequestSizeLimit(32_768)]
public sealed class TenantManagementController(ITenantChannelManagementOperations operations,
    ITenantManagementOAuth oauth) : ControllerBase
{
    [HttpGet("summary")]
    public Task<JsonElement> Summary(CancellationToken cancellationToken) => operations.Summary(cancellationToken);

    [HttpPost("oauth/start")]
    public async Task<JsonElement> StartOAuth(TenantManagementOAuthStartRequest request, CancellationToken cancellationToken)
    {
        var result = await oauth.Start(ActorId, request.EnableOrderAcceptance, cancellationToken);
        return ProviderJson.Encode(new { flowId = result.FlowId, authorizationUrl = result.AuthorizationUrl, expiresAt = result.ExpiresAt });
    }

    [HttpGet("oauth/flows/{flowId:guid}")]
    public async Task<JsonElement> OAuthFlow(Guid flowId, CancellationToken cancellationToken)
    {
        var result = await oauth.Read(flowId, ActorId, cancellationToken);
        return ProviderJson.Encode(new
        {
            flowId = result.FlowId,
            status = result.Status,
            storeId = result.StoreId,
            storeConfirmed = result.StoreConfirmed,
            createdAt = result.CreatedAt,
            expiresAt = result.ExpiresAt,
            completedAt = result.CompletedAt,
            errorCode = result.ErrorCode
        });
    }

    [HttpGet("catalogue")]
    public Task<JsonElement> Catalogue(CancellationToken cancellationToken) => operations.Catalogue(cancellationToken);

    [HttpPut("catalogue/draft")]
    public Task<JsonElement> SaveDraft(TenantManagementDraftRequest request, CancellationToken cancellationToken)
        => operations.SaveDraft(request, ActorId, cancellationToken);

    [HttpPost("catalogue/preview")]
    public Task<JsonElement> Preview(TenantManagementPreviewRequest request, CancellationToken cancellationToken)
        => operations.Preview(request, ActorId, cancellationToken);

    [HttpPost("catalogue/publish")]
    public Task<JsonElement> Publish(TenantManagementPublishRequest request, CancellationToken cancellationToken)
        => operations.Publish(request, ActorId, cancellationToken);

    [HttpGet("catalogue/publications/{publicationId:guid}")]
    public Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken)
        => operations.Publication(publicationId, cancellationToken);

    [HttpGet("availability")]
    public Task<JsonElement> Availability(CancellationToken cancellationToken) => operations.Availability(cancellationToken);

    [HttpPost("availability/pause")]
    public Task<JsonElement> Pause(TenantManagementPauseRequest request, CancellationToken cancellationToken)
        => operations.Pause(request, ActorId, cancellationToken);

    [HttpPost("availability/resume")]
    public Task<JsonElement> Resume(CancellationToken cancellationToken)
        => operations.Resume(ActorId, cancellationToken);

    [HttpGet("exceptions")]
    public Task<JsonElement> Exceptions([FromQuery] string cursor = "", CancellationToken cancellationToken = default)
        => operations.Exceptions(cursor, cancellationToken);

    [HttpGet("exceptions/{exceptionId:guid}")]
    public Task<JsonElement> Exception(Guid exceptionId, CancellationToken cancellationToken)
        => operations.Exception(exceptionId, cancellationToken);

    [HttpPost("exceptions/{exceptionId:guid}/reconcile")]
    public Task<JsonElement> Reconcile(Guid exceptionId, CancellationToken cancellationToken)
        => operations.Reconcile(exceptionId, ActorId, cancellationToken);

    [HttpPost("disconnect")]
    public Task<JsonElement> Disconnect(TenantManagementDisconnectRequest request, CancellationToken cancellationToken)
        => operations.Disconnect(request.StoreId, ActorId, cancellationToken);

    private Guid ActorId => HttpContext.Items.TryGetValue(TenantManagementGatewayMiddleware.ActorContextKey, out var value)
        && value is Guid actorId && actorId != Guid.Empty ? actorId
        : throw new ChannelConsoleException(403, "Tenant management actor is required.");
}

public sealed record TenantManagementOAuthStartRequest(bool EnableOrderAcceptance = false);
