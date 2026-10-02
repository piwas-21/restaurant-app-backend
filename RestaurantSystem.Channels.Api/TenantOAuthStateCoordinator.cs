using Microsoft.AspNetCore.WebUtilities;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantOAuthStateCoordinator(TenantManagementContext context, ITenantOAuthFlows flows,
    ITenantOAuthProvider provider, IChannelAvailabilityJobs jobs,
    IChannelManagementConnectionState connectionState, IChannelManagementAudit audit) : ITenantOAuthStateCoordinator
{
    private const string Pending = "Pending";
    private const string Processing = "Processing";
    private const string Connected = "Connected";
    private const string Failed = "Failed";
    private const string Expired = "Expired";
    private const string ConnectionUnconfirmed = "ConnectionUnconfirmed";
    private const string AuthorizationDenied = "AuthorizationDenied";
    private const string Busy = "ConnectionOperationBusy";
    private const string AuthorizationSuperseded = "AuthorizationSuperseded";
    private const string OAuthAction = "OAuth";
    private const string CallbackAction = "OAuthCallback";
    private const string ConnectionStateAction = "OAuthConnectionState";
    private const string OAuthStartAction = "OAuthStart";
    private const string Intent = "Intent";
    private const string NoOpCode = "NoOp";

    private AvailabilityBinding Binding => context.Binding();
    private TenantManagementGatewaySettings Management => context.Management;

    public async Task<TenantOAuthStart> Start(Guid actorId, bool enableOrderAcceptance,
        CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        if (actorId == Guid.Empty) throw Disabled();
        await using var lease = await jobs.TryLease(Binding, cancellationToken);
        if (lease is null) throw OperationBusy();
        var flowId = Guid.NewGuid();
        await audit.Record(Binding, actorId, OAuthStartAction, Intent, flowId, context.Clock.GetUtcNow(), cancellationToken);
        var prepared = await PrepareAuthorization(flowId, actorId, enableOrderAcceptance, cancellationToken);
        var flow = prepared.Flow;
        await SupersedePrevious(flow, cancellationToken);
        await flows.Create(flow, cancellationToken);
        await audit.Record(Binding, actorId, OAuthStartAction, Pending, flow.Id, context.Clock.GetUtcNow(), cancellationToken);
        return new(flow.Id, prepared.AuthorizationUrl, flow.ExpiresAt);
    }

    private async Task<TenantOAuthPreparation> PrepareAuthorization(Guid flowId, Guid actorId,
        bool enableOrderAcceptance, CancellationToken cancellationToken)
    {
        try { return await provider.Prepare(flowId, actorId, enableOrderAcceptance, cancellationToken); }
        catch (ChannelConsoleException)
        {
            await audit.Record(Binding, actorId, OAuthStartAction, "PreflightFailed", flowId,
                context.Clock.GetUtcNow(), cancellationToken);
            throw;
        }
        catch (HttpRequestException)
        {
            await audit.Record(Binding, actorId, OAuthStartAction, "PreflightUnconfirmed", flowId,
                context.Clock.GetUtcNow(), cancellationToken);
            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await audit.Record(Binding, actorId, OAuthStartAction, "PreflightUnconfirmed", flowId,
                context.Clock.GetUtcNow(), cancellationToken);
            throw;
        }
    }

    private async Task SupersedePrevious(TenantOAuthFlow flow, CancellationToken cancellationToken)
    {
        var count = await flows.CancelPending(Binding, AuthorizationSuperseded, context.Clock.GetUtcNow(), cancellationToken);
        var outcome = count == 0 ? "NoPreviousFlow" : "SupersededPreviousFlows";
        await audit.Record(Binding, flow.ActorId, OAuthStartAction, outcome, flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
    }

    public async Task<TenantOAuthStatus> Read(Guid flowId, Guid actorId, CancellationToken cancellationToken)
    {
        var flow = await flows.Read(Binding, flowId, cancellationToken);
        EnsureActor(flow, actorId);
        if (flow!.ExpiresAt <= context.Clock.GetUtcNow() && flow.Status is Pending or Processing)
            flow = await ExpireIfCurrent(flow, actorId, cancellationToken);
        return Status(flow);
    }

    public async Task<TenantOAuthCallback?> CompleteIfKnown(string state, string code, string error,
        CancellationToken cancellationToken)
    {
        if (!Management.Enabled || !context.Bridge.Enabled || state.Length is < 32 or > 128) return null;
        var known = await flows.FindByStateHash(Binding, provider.HashState(state), cancellationToken);
        if (known is null) return null;
        await using var lease = await jobs.TryLease(Binding, cancellationToken);
        if (lease is null) return await FailBusy(known, cancellationToken);
        return await ProcessKnownCallback(state, code, error, cancellationToken);
    }

    private async Task<TenantOAuthCallback?> ProcessKnownCallback(string state, string code, string error,
        CancellationToken cancellationToken)
    {
        var known = await flows.FindByStateHash(Binding, provider.HashState(state), cancellationToken);
        if (known is null) return null;
        await audit.Record(Binding, known.ActorId, OAuthAction, "CallbackIntent", known.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        var flow = await flows.Claim(Binding, known.StateHash, context.Clock.GetUtcNow(), cancellationToken);
        if (flow is null) return await NoOp(known, cancellationToken);
        if (flow.Status == Expired) return await ExpiredCallback(flow, cancellationToken);
        if (flow.Status != Processing) return await NoOp(flow, cancellationToken);
        if (!ValidCallback(code, error))
        {
            await Finish(flow, Failed, AuthorizationDenied, cancellationToken);
            return Callback(flow.Id);
        }
        await ConnectAndFinish(flow, code, cancellationToken);
        return Callback(flow.Id);
    }

    private async Task ConnectAndFinish(TenantOAuthFlow flow, string code, CancellationToken cancellationToken)
    {
        try
        {
            await provider.Connect(flow, code, cancellationToken);
            await audit.Record(Binding, flow.ActorId, ConnectionStateAction, Intent, flow.Id,
                context.Clock.GetUtcNow(), cancellationToken);
            if (!await Finish(flow, Connected, null, cancellationToken)) return;
            await connectionState.Set(Binding, false, flow.ActorId, context.Clock.GetUtcNow(), cancellationToken);
            await audit.Record(Binding, flow.ActorId, ConnectionStateAction, Connected, flow.Id,
                context.Clock.GetUtcNow(), cancellationToken);
        }
        catch (ChannelConsoleException)
        {
            await Finish(flow, Failed, ConnectionUnconfirmed, cancellationToken);
        }
        catch (HttpRequestException)
        {
            await Finish(flow, Failed, ConnectionUnconfirmed, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Finish(flow, Failed, ConnectionUnconfirmed, cancellationToken);
        }
    }

    private async Task<TenantOAuthFlow> ExpireIfCurrent(TenantOAuthFlow flow, Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var lease = await jobs.TryLease(Binding, cancellationToken);
        if (lease is null) return flow;
        flow = await flows.Read(Binding, flow.Id, cancellationToken) ?? flow;
        EnsureActor(flow, actorId);
        if (flow.ExpiresAt > context.Clock.GetUtcNow() || flow.Status is not (Pending or Processing)) return flow;
        await audit.Record(Binding, actorId, OAuthAction, "ExpiryIntent", flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        await flows.Expire(Binding, flow.Id, context.Clock.GetUtcNow(), cancellationToken);
        flow = await flows.Read(Binding, flow.Id, cancellationToken) ?? flow;
        await audit.Record(Binding, actorId, OAuthAction, flow.Status, flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        return flow;
    }

    private async Task<TenantOAuthCallback> FailBusy(TenantOAuthFlow known, CancellationToken cancellationToken)
    {
        var now = context.Clock.GetUtcNow();
        await audit.Record(Binding, known.ActorId, OAuthAction, "CallbackBusyIntent", known.Id, now, cancellationToken);
        var failed = await flows.FailPending(Binding, known.Id, known.StateHash, Busy, now, cancellationToken);
        await audit.Record(Binding, known.ActorId, failed ? OAuthAction : CallbackAction,
            failed ? Failed : "BusyNoOp", known.Id, context.Clock.GetUtcNow(), cancellationToken);
        return Callback(known.Id);
    }

    private async Task<TenantOAuthCallback> ExpiredCallback(TenantOAuthFlow flow, CancellationToken cancellationToken)
    {
        await audit.Record(Binding, flow.ActorId, OAuthAction, Expired, flow.Id, context.Clock.GetUtcNow(), cancellationToken);
        return Callback(flow.Id);
    }

    private async Task<TenantOAuthCallback> NoOp(TenantOAuthFlow flow, CancellationToken cancellationToken)
    {
        await audit.Record(Binding, flow.ActorId, CallbackAction, NoOpCode, flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        return Callback(flow.Id);
    }

    private async Task<bool> Finish(TenantOAuthFlow flow, string status, string? code,
        CancellationToken cancellationToken)
    {
        await audit.Record(Binding, flow.ActorId, OAuthAction, "FinishIntent", flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        if (!await flows.Finish(Binding, flow.Id, status, code, context.Clock.GetUtcNow(), cancellationToken))
        {
            await audit.Record(Binding, flow.ActorId, OAuthAction, "FinishSkipped", flow.Id,
                context.Clock.GetUtcNow(), cancellationToken);
            return false;
        }
        await audit.Record(Binding, flow.ActorId, OAuthAction, status, flow.Id,
            context.Clock.GetUtcNow(), cancellationToken);
        return true;
    }

    private TenantOAuthCallback Callback(Guid id)
        => new(id, QueryHelpers.AddQueryString(Management.ReturnUrl, "flowId", id.ToString("D")));

    private TenantOAuthStatus Status(TenantOAuthFlow flow)
        => new(flow.Id, flow.Status switch
        {
            Pending or Processing => "pending",
            Connected => "connected",
            Expired => "expired",
            _ => "failed"
        }, context.ConfiguredStore.StoreId, flow.Status == Connected,
            flow.CreatedAt, flow.ExpiresAt, flow.CompletedAt, flow.ErrorCode);

    private static void EnsureActor(TenantOAuthFlow? flow, Guid actorId)
    {
        if (flow is null || flow.ActorId != actorId || actorId == Guid.Empty)
            throw new ChannelConsoleException(404, "Authorization flow was not found.");
    }

    private static bool ValidCallback(string code, string error)
        => code.Length is > 0 and <= 8192 && error.Length <= 128 && error.Length == 0;

    private static ChannelConsoleException Disabled() => new(404, "Tenant delivery authorization is not enabled.");
    private static ChannelConsoleException OperationBusy()
        => new(409, "A channel operation is in progress. Retry authorization after it finishes.");
}
