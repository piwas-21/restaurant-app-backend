using System.Net.Http.Headers;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantOrderClient(HttpClient client) : ITenantOrderClient
{
    private const int MaxReplyBytes = 65_536;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest order, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(store.BaseUrl), "/api/delivery-channels/orders"))
        { Content = JsonContent.Create(order) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", store.ApiToken);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ChannelConsoleException((int)response.StatusCode, "The tenant import has not been confirmed.");
            if (response.Content.Headers.ContentLength > MaxReplyBytes) throw InvalidReply();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + read > MaxReplyBytes) throw InvalidReply();
                await bytes.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            using var json = JsonDocument.Parse(bytes.ToArray());
            var body = json.RootElement;
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("orderId", out var id)
                || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var orderId) || orderId == Guid.Empty
                || !body.TryGetProperty("alreadyImported", out var replay) || replay.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw InvalidReply();
            return new(orderId, replay.GetBoolean());
        }
        catch (HttpRequestException) { throw new ChannelConsoleException(502, "The tenant could not be reached; the prepared import remains retryable."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ChannelConsoleException(504, "The tenant import outcome is uncertain; retry the same prepared request."); }
        catch (JsonException) { throw InvalidReply(); }
    }

    private static ChannelConsoleException InvalidReply() => new(502, "The tenant import reply did not confirm a durable order identity.");
}
