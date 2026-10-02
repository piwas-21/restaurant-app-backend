using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelAvailabilitySafetyTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task StockStatusIsSessionProtectedAndDisabledDefaultsExposeNoCredentialsOrFalseVerification()
    {
        using var anonymous = await Client.GetAsync("/api/sandbox/uber/availability");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await Login(); using var result = await Client.GetAsync("/api/sandbox/uber/availability");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await Json(result); Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("token", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Provider.Calls); Assert.Empty(Provider.Grants);
    }

    [Fact]
    public async Task WorkerFailureNeverLogsProviderPayloadOrCredentials()
    {
        await using var services = new ServiceCollection().AddScoped<IChannelAvailabilityProcessor>(_ => new FaultingProcessor()).BuildServiceProvider();
        var logger = new CaptureLogger();
        using var worker = new ChannelAvailabilityWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenantBridgeSettings { AvailabilityPollSeconds = 10 }), TimeProvider.System, logger);
        await worker.StartAsync(default);
        var message = await logger.Emitted.Task.WaitAsync(TimeSpan.FromSeconds(2)); await worker.StopAsync(default);
        Assert.DoesNotContain(FaultingProcessor.PrivateFixture, message, StringComparison.Ordinal);
        Assert.Equal("Sandbox availability synchronization failed; review stock status and source permissions.", message);
        Assert.Null(logger.Exception);
    }

    [Theory]
    [InlineData("disabled-opt-in")]
    [InlineData("missing-read-token")]
    [InlineData("fast-poll")]
    [InlineData("slow-poll")]
    [InlineData("no-writes")]
    [InlineData("excess-writes")]
    public void InvalidOptInOrUnboundedCadenceFailsConfiguration(string mode)
    {
        var settings = new TenantBridgeSettings(); Assert.True(settings.IsValid());
        if (mode == "disabled-opt-in") { settings.SyncAvailability = true; settings.Store.CatalogueApiToken = "public-fixture"; }
        if (mode == "missing-read-token") { settings.Enabled = true; settings.SyncAvailability = true; }
        if (mode == "fast-poll") settings.AvailabilityPollSeconds = 9;
        if (mode == "slow-poll") settings.AvailabilityPollSeconds = 301;
        if (mode == "no-writes") settings.AvailabilityMaxWrites = 0;
        if (mode == "excess-writes") settings.AvailabilityMaxWrites = 21;
        Assert.False(settings.IsValid());
    }

    private sealed class FaultingProcessor : IChannelAvailabilityProcessor
    {
        public const string PrivateFixture = "fixture-phone-and-stock-token-that-must-not-reach-logs";
        public Task Process(CancellationToken cancellationToken) => Task.FromException(new ChannelConsoleException(502, PrivateFixture));
    }
    private sealed class CaptureLogger : ILogger<ChannelAvailabilityWorker>
    {
        public TaskCompletionSource<string> Emitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Exception = exception; Emitted.TrySetResult(formatter(state, exception)); }
    }
}
