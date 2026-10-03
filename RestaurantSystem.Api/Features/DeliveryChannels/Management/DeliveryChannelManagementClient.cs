using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Management;

public sealed class DeliveryChannelManagementClient(
    HttpClient http,
    IOptions<DeliveryChannelManagementSettings> managementOptions,
    IOptions<DeliveryChannelSettings> channelOptions) : IDeliveryChannelManagementClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> Send<TResponse>(HttpMethod method, string path, Guid actorId,
        object? body, CancellationToken cancellationToken)
    {
        var settings = managementOptions.Value;
        if (!settings.Enabled) throw new NotFoundException("Delivery channel management is not enabled.");
        var store = channelOptions.Value.Stores.Single();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ServerCredential);
        request.Headers.Add("X-Sofra-Tenant-Id", settings.TenantId);
        request.Headers.Add("X-Uber-Store-Id", store.StoreId);
        request.Headers.Add("X-Sofra-Actor-Id", actorId.ToString("D"));
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Unavailable(); }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw Map(response.StatusCode, content);
            try
            {
                return JsonSerializer.Deserialize<TResponse>(content, JsonOptions)
                    ?? throw Unavailable();
            }
            catch (JsonException) { throw Unavailable(); }
        }
    }

    private static Exception Map(HttpStatusCode status, string content)
    {
        var message = SafeMessage(content);
        var code = SafeErrorCode(content);
        return status switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new BadRequestException(message) { ErrorCode = code },
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ForbiddenException(message),
            HttpStatusCode.NotFound => new NotFoundException(message),
            HttpStatusCode.Conflict => code is null ? new ConflictException(message) : new ConflictException(message, code),
            HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout => Unavailable(),
            _ => Unavailable(),
        };
    }

    private static string? SafeErrorCode(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errorCode", out var code)
                || code.ValueKind != JsonValueKind.String) return null;
            return code.GetString() is "SelectionLimitExceeded" or "SelectionOverrideLimitExceeded"
                or "CategoryLimitExceeded" or "SelectionRequired" or "SourceRevisionChanged"
                or "DraftRevisionConflict" or "TaxProfileChanged" or "TaxProfileConfirmationRequired"
                or "ReviewedTaxProfileUnavailable" or "SelectionBlocked"
                ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string SafeMessage(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var message = value.GetString();
                if (!string.IsNullOrWhiteSpace(message) && message.Length <= 500 && !message.Any(char.IsControl))
                    return message;
            }
        }
        catch (JsonException)
        {
            // Malformed gateway bodies use the safe fallback; never expose an untrusted response.
        }
        return "The delivery integration could not complete this action. Refresh its status before retrying.";
    }

    private static ServiceUnavailableException Unavailable()
        => new("The delivery integration gateway is unavailable. Refresh its status before retrying.");
}
