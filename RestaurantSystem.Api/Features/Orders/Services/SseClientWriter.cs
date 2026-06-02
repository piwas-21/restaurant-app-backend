using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Default <see cref="ISseClientWriter"/>. Faithful extraction of
/// OrderEventService.SendToClient; the write lock / 5s timeout / error-tracking / disconnect
/// signalling logic is unchanged. Error recording now goes through the thread-safe
/// <see cref="SseClient.RecordError"/> instead of a raw list mutation.
/// </summary>
public class SseClientWriter : ISseClientWriter
{
    private readonly ISseActivityLog _activityLog;
    private readonly ILogger<SseClientWriter> _logger;

    public SseClientWriter(ISseActivityLog activityLog, ILogger<SseClientWriter> logger)
    {
        _activityLog = activityLog;
        _logger = logger;
    }

    public async Task SendAsync(SseClient client, byte[] eventBytes, string? eventType = null)
    {
        // Skip if client is already marked for disconnection
        if (client.DisconnectCts.IsCancellationRequested)
        {
            _logger.LogDebug("Skipping send to client {ClientId} - already marked for disconnection", client.ClientId);
            return;
        }

        try
        {
            var sendingMsg = $"Sending event to client {client.ClientId} ({client.ClientType}), {eventBytes.Length} bytes";
            _logger.LogInformation(sendingMsg);
            _activityLog.Add("Info", sendingMsg, eventType, client.ClientId);

            // Link the 5-second write timeout with the client's disconnect token so that
            // a client disconnection aborts the write immediately rather than waiting the
            // full 5 seconds for the timeout to fire.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, client.DisconnectCts.Token);

            // Acquire lock to prevent concurrent writes (heartbeat vs event)
            await client.WriteLock.WaitAsync(linkedCts.Token);
            try
            {
                await client.Response.Body.WriteAsync(eventBytes, linkedCts.Token);
                await client.Response.Body.FlushAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (client.DisconnectCts.Token.IsCancellationRequested)
            {
                _logger.LogDebug("Send cancelled for client {ClientId} due to client disconnection", client.ClientId);
                return;
            }
            finally
            {
                client.WriteLock.Release();
            }

            // Track successful send
            client.SuccessfulSends++;
            client.LastEventSentAt = DateTime.UtcNow;
            client.LastActivityAt = DateTime.UtcNow; // Update activity timestamp to prevent stale cleanup

            var successMsg = $"✓ Event successfully sent to client {client.ClientId}";
            _logger.LogInformation(successMsg);
            _activityLog.Add("Info", successMsg, eventType, client.ClientId);
        }
        catch (OperationCanceledException)
        {
            var timeoutMsg = $"✗ Timeout sending event to client {client.ClientId} - signaling disconnect";
            _logger.LogWarning(timeoutMsg);
            _activityLog.Add("Warning", timeoutMsg, eventType, client.ClientId);

            // Track error (RecordError keeps only the last 10 per client)
            client.FailedSends++;
            client.RecordError(new ClientError
            {
                Timestamp = DateTime.UtcNow,
                ErrorType = "Timeout",
                Message = "Timeout sending event (5 seconds exceeded)",
                EventType = eventType
            });

            // Signal disconnect - this will cause the heartbeat loop to exit and clean up properly
            client.DisconnectCts.Cancel();
        }
        catch (Exception ex)
        {
            var errorMsg = $"✗ Failed to send event to client {client.ClientId} - signaling disconnect: {ex.Message}";
            _logger.LogError(ex, "✗ Failed to send event to client {ClientId} - signaling disconnect", client.ClientId);
            _activityLog.Add("Error", errorMsg, eventType, client.ClientId);

            // Track error (RecordError keeps only the last 10 per client)
            client.FailedSends++;
            client.RecordError(new ClientError
            {
                Timestamp = DateTime.UtcNow,
                ErrorType = ex.GetType().Name,
                Message = ex.Message,
                EventType = eventType
            });

            // Signal disconnect - this will cause the heartbeat loop to exit and clean up properly
            client.DisconnectCts.Cancel();
        }
    }
}
