using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IOrderEventService _orderEventService;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IOrderEventService orderEventService, ILogger<EventsController> logger)
    {
        _orderEventService = orderEventService;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to kitchen order events
    /// </summary>
    [HttpGet("kitchen")]
    [Produces("text/event-stream")]
    [Authorize(Roles = "Admin,KitchenStaff,Server")]
    public async Task KitchenEvents(CancellationToken cancellationToken)
    {
        await SetupSseConnection(OrderEventService.ClientType.Kitchen, cancellationToken);
    }

    /// <summary>
    /// Subscribe to stock update events
    /// </summary>
    [HttpGet("stock")]
    [Produces("text/event-stream")]
    [Authorize]
    public async Task StockEvents(CancellationToken cancellationToken)
    {
        await SetupSseConnection(OrderEventService.ClientType.Stock, cancellationToken);
    }

    /// <summary>
    /// Subscribe to service order events
    /// </summary>
    [HttpGet("service")]
    [Produces("text/event-stream")]
    [Authorize]
    public async Task ServiceEvents(CancellationToken cancellationToken)
    {
        await SetupSseConnection(OrderEventService.ClientType.Service, cancellationToken);
    }

    /// <summary>
    /// Subscribe to all order events (for managers)
    /// </summary>
    [HttpGet("all")]
    [Produces("text/event-stream")]
    [RequireAdmin]
    public async Task AllEvents(CancellationToken cancellationToken)
    {
        await SetupSseConnection(OrderEventService.ClientType.Manager, cancellationToken);
    }

    /// <summary>
    /// Get diagnostic info about connected clients
    /// </summary>
    [HttpGet("diagnostics")]
    [RequireAdmin]
    public IActionResult GetDiagnostics()
    {
        var stats = _orderEventService.GetClientStatistics();
        return Ok(stats);
    }

    /// <summary>
    /// Send a test event to all connected clients (for debugging SSE connections)
    /// </summary>
    [HttpPost("test-broadcast")]
    [RequireAdmin]
    public async Task<IActionResult> TestBroadcast([FromQuery] string clientType = "Kitchen")
    {
        var testOrder = new OrderDto
        {
            Id = Guid.NewGuid(),
            OrderNumber = "TEST-" + DateTime.UtcNow.ToString("HHmmss"),
            Status = "Pending",
            Type = "Delivery",
            CustomerName = "Test Customer",
            RemainingAmount = 99.99m,
            OrderDate = DateTime.UtcNow
        };

        _logger.LogInformation("Sending test order broadcast to {ClientType} clients", clientType);

        await _orderEventService.NotifyOrderCreated(testOrder);

        var stats = _orderEventService.GetClientStatistics();

        return Ok(new
        {
            message = "Test event sent",
            testOrder,
            connectedClients = stats
        });
    }

    private async Task SetupSseConnection(OrderEventService.ClientType clientType, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();

        // Set response headers BEFORE writing any data
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable Nginx buffering
        Response.Headers["Access-Control-Allow-Origin"] = "*";

        // Disable ASP.NET response buffering for real-time streaming
        var bufferingFeature = Response.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        // Get client IP address (handle proxy forwarded headers)
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        if (HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            ipAddress = forwardedFor.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim() ?? ipAddress;
        }

        // Get country from IP (placeholder - integrate with GeoIP service if needed)
        string? country = GetCountryFromIp(ipAddress);

        var client = new OrderEventService.SseClient
        {
            ClientId = clientId,
            Response = Response,
            ClientType = clientType,
            ConnectedAt = DateTime.UtcNow,
            IpAddress = ipAddress,
            Country = country
        };

        if (!_orderEventService.TryAddClient(clientId, client))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new { error = "Too many SSE connections from your IP address." });
            return;
        }

        _logger.LogInformation("SSE client connected: {ClientId} with type {ClientType}", clientId, clientType);

        try
        {
            // Send initial connection event
            var connectionData = new
            {
                clientId,
                clientType = clientType.ToString(),
                timestamp = DateTime.UtcNow
            };
            var connectionJson = System.Text.Json.JsonSerializer.Serialize(connectionData,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            // Send initial connection event with lock
            await client.WriteLock.WaitAsync(cancellationToken);
            try
            {
                await Response.WriteAsync($"event: connected\ndata: {connectionJson}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                client.LastActivityAt = DateTime.UtcNow; // Update activity timestamp
            }
            finally
            {
                client.WriteLock.Release();
            }

            // Replay any recent events the client might have missed during reconnection
            // This ensures page refreshes don't miss events that occurred during the connection gap
            await _orderEventService.ReplayRecentEventsAsync(client);

            // Create linked token that will cancel if either the request is cancelled OR the client disconnect is signaled
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, client.DisconnectCts.Token);

            // Keep connection alive with heartbeats (every 10 seconds to ensure printer app 45s timeout doesn't trigger)
            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(10000, linkedCts.Token);

                // Send proper SSE event format (not just a comment) so frontend can track heartbeats
                var heartbeatData = new { timestamp = DateTime.UtcNow };
                var heartbeatJson = System.Text.Json.JsonSerializer.Serialize(heartbeatData,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

                // Acquire lock to prevent concurrent writes with events
                await client.WriteLock.WaitAsync(linkedCts.Token);
                try
                {
                    await Response.WriteAsync($"event: heartbeat\ndata: {heartbeatJson}\n\n", linkedCts.Token);
                    await Response.Body.FlushAsync(linkedCts.Token);
                    client.LastActivityAt = DateTime.UtcNow; // Update activity timestamp
                }
                finally
                {
                    client.WriteLock.Release();
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Normal disconnect - client closed connection or server shutting down
            _logger.LogInformation("SSE client disconnected normally: {ClientId}", clientId);
        }
        catch (OperationCanceledException)
        {
            // Also normal for connection close
            _logger.LogInformation("SSE client connection closed: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSE connection error for client {ClientId}", clientId);
        }
        finally
        {
            _logger.LogInformation("SSE client cleanup: {ClientId}", clientId);
            _orderEventService.RemoveClient(clientId);
        }
    }

    private string? GetCountryFromIp(string ipAddress)
    {
        // Placeholder for GeoIP integration
        // You can integrate with services like:
        // - MaxMind GeoIP2
        // - IP2Location
        // - ipapi.co
        // - ip-api.com

        // For localhost/private IPs, return a default
        if (ipAddress == "Unknown" || ipAddress.StartsWith("127.") || ipAddress.StartsWith("192.168.") ||
            ipAddress.StartsWith("10.") || ipAddress == "::1" || ipAddress.StartsWith("172."))
        {
            return "Local";
        }

        // TODO: Implement actual GeoIP lookup
        return null;
    }
}
