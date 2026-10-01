using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class SandboxOrderTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task MenuUploadRequiresVerifiedReadbackAndBlocksEnablingWithDifferentMenu()
    {
        await Assert.ThrowsAsync<ChannelConsoleException>(() => EnableThroughOAuth());
        Assert.DoesNotContain(Provider.Calls, c => c.Method == HttpMethod.Patch);
        Assert.All(Provider.Calls.Where(c => c.Method == HttpMethod.Post), c => Assert.True(c.Body!.Value.GetProperty("require_manual_acceptance").GetBoolean()));
        Provider.CorruptMenuReadback = true;
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxMenu>().Publish(default)));
        Provider.CorruptMenuReadback = false;
        var published = await InScope(s => s.GetRequiredService<ISandboxMenu>().Publish(default)); Assert.True(published.GetProperty("verified").GetBoolean());
        await EnableThroughOAuth();
        var nomination = Provider.Calls.Last(c => c.Method == HttpMethod.Post);
        Assert.False(nomination.Body!.Value.GetProperty("require_manual_acceptance").GetBoolean());
        Assert.DoesNotContain(Provider.Calls, c => c.Method == HttpMethod.Patch);
        await InScope(s => s.GetRequiredService<ISandboxConnection>().EnableOrders(false, default));
        var pause = Assert.Single(Provider.Calls, c => c.Method == HttpMethod.Patch);
        Assert.False(pause.Body!.Value.GetProperty("is_order_manager").GetBoolean());
        Assert.False(pause.Body.Value.GetProperty("integration_enabled").GetBoolean());
    }

    [Fact]
    public async Task OrderRetrievalNeedsSignedStoreReceiptAndReturnedStoreBindingPreservesInstructions()
    {
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Read(Guid.NewGuid().ToString(), default)));
        Assert.Empty(Provider.Calls);
        var orderId = await SeedOrder(); Provider.OrderStoreId = Guid.NewGuid();
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Read(orderId, default)));
        Provider.OrderStoreId = GatewayFixture.StoreId;
        var order = await InScope(s => s.GetRequiredService<ISandboxOrders>().Read(orderId, default));
        Assert.Equal("No peanuts; severe allergy", order.GetProperty("cart").GetProperty("items")[0].GetProperty("special_instructions").GetString());
        Assert.Equal("Please read item notes", order.GetProperty("special_instructions").GetString());
        Assert.All(Provider.Calls.Where(c => c.Path.Contains("/order/", StringComparison.Ordinal)), c => Assert.StartsWith("/v2/eats/order/", c.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentAndOppositeDecisionsAreSentAtMostOnce()
    {
        var orderId = await SeedOrder();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { await InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "accept", "Fixture acceptance", default)); return true; }
            catch (ChannelConsoleException ex) { Assert.Equal(409, ex.Status); return false; }
        }));
        Assert.Single(results, r => r); Assert.Single(Provider.Calls, c => c.Method == HttpMethod.Post);
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "deny", "Opposite decision", default)));
        var record = await InScope(s => s.GetRequiredService<IConsoleRepository>().FindAction(GatewayFixture.ClientId, GatewayFixture.StoreId, orderId, default));
        Assert.Equal(new OrderAction("accept", "Succeeded"), record);
    }

    [Fact]
    public async Task AmbiguousDecisionIsNotRetriedAndOnlyMatchingReadbackReconcilesIt()
    {
        var orderId = await SeedOrder(); Provider.ThrowOnDecision = true;
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "accept", "Timeout test", default)));
        Assert.Equal("Unknown", (await Record(orderId))!.State);
        Provider.ThrowOnDecision = false;
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "deny", "Do not double send", default)));
        Assert.Single(Provider.Calls, c => c.Method == HttpMethod.Post);
        Provider.OrderState = "DENIED";
        await InScope(s => s.GetRequiredService<ISandboxOrders>().Read(orderId, default)); Assert.Equal("Unknown", (await Record(orderId))!.State);
        Provider.OrderState = "ACCEPTED";
        await InScope(s => s.GetRequiredService<ISandboxOrders>().Read(orderId, default)); Assert.Equal("Succeeded", (await Record(orderId))!.State);
    }

    [Fact]
    public async Task ExplicitProviderRejectionAllowsManualRetryAndDenialUsesDocumentedReason()
    {
        var orderId = await SeedOrder(); Provider.DecisionStatus = 400;
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "deny", "Fixture denial", default)));
        Assert.Equal("Failed", (await Record(orderId))!.State);
        Provider.DecisionStatus = 204;
        await InScope(s => s.GetRequiredService<ISandboxOrders>().Decide(orderId, "deny", "Fixture denial retry", default));
        Assert.Equal("Succeeded", (await Record(orderId))!.State);
        var last = Provider.Calls.Last(c => c.Method == HttpMethod.Post);
        Assert.EndsWith("/deny_pos_order", last.Path, StringComparison.Ordinal);
        Assert.Equal("OTHER", last.Body!.Value.GetProperty("reason").GetProperty("code").GetString());
    }

    private Task<OrderAction?> Record(string orderId)
        => InScope(s => s.GetRequiredService<IConsoleRepository>().FindAction(GatewayFixture.ClientId, GatewayFixture.StoreId, orderId, default));

    [Fact]
    public async Task UnwindingRejectedAttemptCannotMarkNewPendingRetryUncertain()
    {
        var orderId = await SeedOrder();
        await using var data = NpgsqlDataSource.Create(Database.ConnectionString);
        var barrier = new DecisionBarrierRepository(new PostgresConsoleRepository(data));
        using var host = Host.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IConsoleRepository>(barrier)));
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Provider.BeforeDecision = async () =>
        {
            if (Interlocked.Increment(ref calls) == 1) { Provider.DecisionStatus = 400; return; }
            Provider.DecisionStatus = 204; retryStarted.SetResult();
            await releaseRetry.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ISandboxOrders>().Decide(orderId, "accept", "Rejected first attempt", default);
        await barrier.FailedWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondScope.ServiceProvider.GetRequiredService<ISandboxOrders>().Decide(orderId, "accept", "Explicit retry", default);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        barrier.ReleaseFailedWriter.SetResult();
        await Assert.ThrowsAsync<ChannelConsoleException>(() => first);
        Assert.Equal("Pending", (await Record(orderId))!.State);
        releaseRetry.SetResult(); await second;
        Assert.Equal("Succeeded", (await Record(orderId))!.State);
    }

    private async Task EnableThroughOAuth()
    {
        var session = await Session();
        var url = await InScope(s => s.GetRequiredService<ISandboxConnection>().Start(session, default, true));
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        await InScope(async s => { await s.GetRequiredService<ISandboxConnection>().Complete(session, state, "public-fixture-code", "", default); return true; });
    }
}
