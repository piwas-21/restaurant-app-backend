using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelTransport(HttpClient client, IOptions<TenantBridgeSettings> settings) : ITenantChannelTransport
{
    private const int MaxReplyBytes = 65_536;
    private const int MaxCatalogueReplyBytes = 2_097_152;
    private const int MaxErrorBytes = 65_536;
    private const string SelectionLimitCode = "SelectionLimitExceeded";
    private const string SelectionOverrideLimitCode = "SelectionOverrideLimitExceeded";
    private const string SourceChangedCode = "SourceRevisionChanged";
    private const string CategoryLimitCode = "CategoryLimitExceeded";

    public async Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maximumReplyBytes = path is "/api/delivery-channels/catalogue/snapshot"
            or "/api/delivery-channels/catalogue/selection-snapshot"
            or "/api/delivery-channels/catalogue/categories/snapshot"
            or "/api/delivery-channels/catalogue/categories/compare-snapshot" ? MaxCatalogueReplyBytes : MaxReplyBytes;
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.HttpTimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(store.BaseUrl), path))
        { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", store.ApiToken);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw await Failure(response, timeout.Token);
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

    private static async Task<ChannelConsoleException> Failure(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = await ReadErrorCode(response.Content, cancellationToken);
        if ((int)response.StatusCode == 400 && code == SelectionLimitCode)
            return new(409, "Select no more than 200 marketplace items.", SelectionLimitCode);
        if ((int)response.StatusCode == 400 && code == SelectionOverrideLimitCode)
            return new(400, "Review no more than 2,000 individual item overrides at a time.", SelectionOverrideLimitCode);
        if ((int)response.StatusCode == 400 && code == CategoryLimitCode)
            return new(400, "This catalogue has too many categories for delivery-channel management.", CategoryLimitCode);
        if ((int)response.StatusCode == 409 && code == SourceChangedCode)
            return new(409, "The tenant catalogue changed. Refresh categories and review the selection again.", SourceChangedCode);
        return new((int)response.StatusCode, "The tenant channel request has not been confirmed.", code);
    }

    private static async Task<string?> ReadErrorCode(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxErrorBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > MaxErrorBytes) return null;
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (!document.RootElement.TryGetProperty("errorCode", out var value)
                || value.ValueKind != JsonValueKind.String) return null;
            return value.GetString() is "SelectionLimitExceeded" or "SelectionOverrideLimitExceeded"
                or "CategoryLimitExceeded" or "SelectionRequired" or "SourceRevisionChanged"
                ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static ChannelConsoleException InvalidReply() => new(502, "The tenant channel reply was malformed or too large.");
}
