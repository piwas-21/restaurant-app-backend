using RestaurantSystem.Api.Features.Orders.Models;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Default <see cref="ISseBroadcastService"/>. Single generic broadcast that replaces the
/// two byte-identical <c>SendEventToClients(StockEvent|OrderEvent, …)</c> overloads in
/// OrderEventService; client selection, the replay-on-no-clients behaviour, the parallel
/// per-client send, success/failure accounting, and logging strings are unchanged. The only
/// difference is that the event-type name is passed explicitly rather than read off the
/// event object (the two event records exposed it under different concrete types).
/// </summary>
public class SseBroadcastService : ISseBroadcastService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISseClientManager _clientManager;
    private readonly ISseEventReplayService _replay;
    private readonly ISseClientWriter _clientWriter;
    private readonly ISseActivityLog _activityLog;
    private readonly ILogger<SseBroadcastService> _logger;

    public SseBroadcastService(
        ISseClientManager clientManager,
        ISseEventReplayService replay,
        ISseClientWriter clientWriter,
        ISseActivityLog activityLog,
        ILogger<SseBroadcastService> logger)
    {
        _clientManager = clientManager;
        _replay = replay;
        _clientWriter = clientWriter;
        _activityLog = activityLog;
        _logger = logger;
    }

    public async Task SendEventToClients<TEvent>(TEvent eventData, ClientType targetClientType, string eventType)
    {
        var json = JsonSerializer.Serialize(eventData, SerializerOptions);

        var eventMessage = $"event: {eventType}\ndata: {json}\n\n";
        var eventBytes = Encoding.UTF8.GetBytes(eventMessage);

        // Create snapshot to avoid race conditions during iteration
        var targetClients = _clientManager.GetClients().Where(c =>
            targetClientType == ClientType.All || c.ClientType == targetClientType || c.ClientType == ClientType.Manager).ToList();

        var broadcastMsg = $"Broadcasting event {eventType} to {targetClients.Count} {targetClientType} client(s): [{string.Join(", ", targetClients.Select(c => c.ClientId))}]";
        _logger.LogInformation(broadcastMsg);
        _activityLog.Add("Info", broadcastMsg, eventType);

        if (targetClients.Count == 0)
        {
            var warnMsg = $"No clients to broadcast event {eventType} for type {targetClientType}";
            _logger.LogWarning(warnMsg);
            _activityLog.Add("Warning", warnMsg, eventType);

            // Still store event for replay - clients that connect soon will receive it
            _replay.StoreEventForReplay(eventBytes, eventType, targetClientType);
            return;
        }

        // Store event for replay to newly connecting clients
        _replay.StoreEventForReplay(eventBytes, eventType, targetClientType);

        // Send to all clients in parallel, but track each individually
        int successCount = 0;
        int failureCount = 0;
        var sendTasks = targetClients.Select(async client =>
        {
            try
            {
                await _clientWriter.SendAsync(client, eventBytes, eventType);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                var errorMsg = $"Unhandled exception in SendToClient for {client.ClientId}";
                _logger.LogError(ex, errorMsg);
                _activityLog.Add("Error", $"{errorMsg}: {ex.Message}", eventType, client.ClientId);
            }
        }).ToArray();

        await Task.WhenAll(sendTasks);

        var completeMsg = $"Event {eventType} broadcast completed: {successCount} succeeded, {failureCount} failed out of {targetClients.Count} clients";
        _logger.LogInformation(completeMsg);
        _activityLog.Add("Info", completeMsg, eventType);
    }
}
