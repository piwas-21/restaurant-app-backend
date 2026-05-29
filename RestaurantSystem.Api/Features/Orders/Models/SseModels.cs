// FILE_LENGTH_EXEMPT: SSE infra models (not /Dtos/) grouped in one file per the Sprint 3.7 plan
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Models;

// Server-Sent-Events models for the order/stock real-time feed. Extracted verbatim from
// the nested types of OrderEventService (Sprint 3 god-class decomposition, task 3.7);
// behaviour and shape are unchanged.

public class SseClient : IDisposable
{
    public required string ClientId { get; set; }
    public required HttpResponse Response { get; set; }
    public ClientType ClientType { get; set; }
    public DateTime ConnectedAt { get; set; }
    public required string IpAddress { get; set; }
    public string? Country { get; set; }

    // Synchronization for concurrent writes (heartbeats vs events)
    public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);

    // Cancellation token to signal when client should disconnect
    public CancellationTokenSource DisconnectCts { get; } = new CancellationTokenSource();

    // Error tracking
    public List<ClientError> Errors { get; } = new List<ClientError>();
    public int SuccessfulSends { get; set; }
    public int FailedSends { get; set; }
    public DateTime? LastEventSentAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            // DisconnectCts and WriteLock are non-nullable, always-initialized members,
            // so no null-conditional access is needed.
            DisconnectCts.Cancel();
            DisconnectCts.Dispose();
            WriteLock.Dispose();
            _disposed = true;
        }
    }
}

public class ClientError
{
    public DateTime Timestamp { get; set; }
    public required string ErrorType { get; set; }
    public required string Message { get; set; }
    public string? EventType { get; set; }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? EventType { get; set; }
    public string? ClientId { get; set; }
}

public class OrderEvent
{
    public required string EventType { get; set; }
    public required OrderDto Order { get; set; }
    public string? PreviousStatus { get; set; }
    public DateTime Timestamp { get; set; }
}

public class StockEvent
{
    public required string EventType { get; set; }
    public string? PreviousStatus { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum ClientType
{
    Kitchen,
    Service,
    Manager,
    Stock,
    All
}

/// <summary>
/// Represents an event stored for replay to newly connecting clients
/// </summary>
public class ReplayableEvent
{
    public required byte[] EventBytes { get; set; }
    public required string EventType { get; set; }
    public ClientType TargetClientType { get; set; }
    public DateTime Timestamp { get; set; }
}
