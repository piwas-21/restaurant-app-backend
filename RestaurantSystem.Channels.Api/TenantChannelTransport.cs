using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelTransport(HttpClient client, IOptions<TenantBridgeSettings> settings) : ITenantChannelTransport
{
    private const int MaxReplyBytes = 65_536;
    private const int MaxCatalogueReplyBytes = 2_097_152;

    public async Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maximumReplyBytes = path == "/api/delivery-channels/catalogue/snapshot" ? MaxCatalogueReplyBytes : MaxReplyBytes;
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.HttpTimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(store.BaseUrl), path))
        { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", store.ApiToken);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ChannelConsoleException((int)response.StatusCode, "The tenant channel request has not been confirmed.");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
            if (response.Content.Headers.ContentLength > maximumReplyBytes) throw InvalidReply();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + read > maximumReplyBytes) throw InvalidReply();
                await bytes.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            using var json = JsonDocument.Parse(bytes.ToArray());
            return json.RootElement.Clone();
        }
        catch (HttpRequestException) { throw new ChannelConsoleException(502, "The tenant channel request could not be confirmed."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ChannelConsoleException(504, "The tenant channel request outcome is uncertain; recover using its durable identity."); }
        catch (JsonException) { throw InvalidReply(); }
    }

    private static ChannelConsoleException InvalidReply() => new(502, "The tenant channel reply was malformed or too large.");
}
