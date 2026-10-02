using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class UberSandboxClient(HttpClient client, IOptions<SandboxConsoleSettings> options) : IUberSandboxClient
{
    public Task<ProviderReply> Token(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
        => Request(new(HttpMethod.Post, new Uri(new Uri(options.Value.AuthBaseUrl), "oauth/v2/token"))
        { Content = new FormUrlEncodedContent(fields) }, cancellationToken);

    public Task<ProviderReply> Send(HttpMethod method, string path, string token, JsonElement? body, CancellationToken cancellationToken)
    {
        if (!path.StartsWith("/v", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Only fixed Uber API paths are supported.", nameof(path));
        var request = new HttpRequestMessage(method, new Uri(new Uri(options.Value.ApiBaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body.Value);
        return Request(request, cancellationToken);
    }

    private async Task<ProviderReply> Request(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.HttpTimeoutSeconds));
            var bounded = timeout.Token;
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded);
                if (response.Content.Headers.ContentLength > options.Value.MaxResponseBytes)
                    throw new ChannelConsoleException(502, "Uber returned a response larger than the sandbox limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(bounded);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                int size;
                while ((size = await stream.ReadAsync(buffer, bounded)) > 0)
                {
                    if (bytes.Length + size > options.Value.MaxResponseBytes)
                        throw new ChannelConsoleException(502, "Uber returned a response larger than the sandbox limit.");
                    await bytes.WriteAsync(buffer.AsMemory(0, size), bounded);
                }
                using var document = JsonDocument.Parse(bytes.Length == 0 ? "{}" : System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
                var clientId = response.Headers.TryGetValues("X-Api-Client-Id", out var values) ? values.SingleOrDefault() ?? string.Empty : string.Empty;
                return new((int)response.StatusCode, document.RootElement.Clone(), clientId);
            }
            catch (HttpRequestException) { throw new ChannelConsoleException(502, "Uber could not be reached. Refresh status before retrying."); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ChannelConsoleException(504, "Uber timed out. Refresh status before retrying."); }
            catch (JsonException) { throw new ChannelConsoleException(502, "Uber returned an unexpected response format."); }
        }
    }
}
