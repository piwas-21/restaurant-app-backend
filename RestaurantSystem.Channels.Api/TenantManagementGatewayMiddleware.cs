using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;
using System.Security.Cryptography;
using Npgsql;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantManagementGatewayMiddleware(RequestDelegate next)
{
    public const string ActorContextKey = "TenantManagementActorId";

    public async Task InvokeAsync(HttpContext context, IOptions<TenantManagementGatewaySettings> management,
        IOptions<TenantBridgeSettings> bridge, IOptions<UberWebhookSettings> webhook, ISandboxCrypto crypto)
    {
        if (!context.Request.Path.StartsWithSegments("/api/tenant-management/uber"))
        {
            await next(context); return;
        }
        context.Response.Headers.CacheControl = "no-store";
        if (!management.Value.Enabled || !bridge.Value.Enabled || !webhook.Value.IsConfigured)
        {
            await Error(context, 404, "Tenant delivery management is not enabled."); return;
        }
        if (!Authenticated(context, management.Value, bridge.Value, webhook.Value, crypto, out var actorId))
        {
            await Error(context, 403, "Tenant management identity or store binding is invalid."); return;
        }
        context.Items[ActorContextKey] = actorId;
        try { await next(context); }
        catch (ChannelConsoleException ex) { await Error(context, ex.Status, ex.Message); }
        catch (NpgsqlException) { await Error(context, 503, "Tenant management storage is unavailable. Refresh status before retrying."); }
        catch (CryptographicException) { await Error(context, 503, "Tenant management protection failed. Contact the administrator."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
    }

    private static bool Authenticated(HttpContext context, TenantManagementGatewaySettings management,
        TenantBridgeSettings bridge, UberWebhookSettings webhook, ISandboxCrypto crypto, out Guid actorId)
    {
        actorId = Guid.Empty;
        var authorization = context.Request.Headers.Authorization.ToString();
        var tenant = context.Request.Headers["X-Sofra-Tenant-Id"].ToString();
        var store = context.Request.Headers["X-Uber-Store-Id"].ToString();
        var actor = context.Request.Headers["X-Sofra-Actor-Id"].ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || !crypto.MatchesHash(header.Parameter ?? string.Empty, management.CredentialHash)
            || !string.Equals(tenant, bridge.Store.TenantId, StringComparison.Ordinal)
            || !Guid.TryParseExact(store, "D", out var storeId) || storeId != bridge.Store.StoreId
            || webhook.StoreIds.Length != 1 || webhook.StoreIds[0] != storeId
            || !Guid.TryParseExact(actor, "D", out actorId) || actorId == Guid.Empty) return false;
        return true;
    }

    private static async Task Error(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { message }, context.RequestAborted);
    }
}
