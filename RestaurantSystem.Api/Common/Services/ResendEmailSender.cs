using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Resend (https://resend.com) email transport. Sends over HTTPS, so it works on hosts
/// that block outbound SMTP. Selected when <c>EmailSettings:Provider</c> is "Resend".
/// The API key + base address are configured on the injected <see cref="HttpClient"/>
/// (see <c>EmailSenderExtensions</c>).
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly EmailSettings _settings;

    public ResendEmailSender(HttpClient http, IOptions<EmailSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var attachments = email.Attachments?
            .Select(a => new ResendAttachment(
                a.FileName,
                Convert.ToBase64String(a.Content),
                a.ContentType,
                a.ContentId))
            .ToList();

        var request = new ResendRequest(
            From: $"{_settings.FromName} <{_settings.FromEmail}>",
            To: [email.To],
            Subject: email.Subject,
            Html: email.HtmlBody,
            Text: email.TextBody,
            Attachments: attachments is { Count: > 0 } ? attachments : null);

        using var response = await _http.PostAsJsonAsync("emails", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new EmailDeliveryException(
                $"Resend API request failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }
    }

    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("attachments")] IReadOnlyList<ResendAttachment>? Attachments);

    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("content_id")] string? ContentId);
}
