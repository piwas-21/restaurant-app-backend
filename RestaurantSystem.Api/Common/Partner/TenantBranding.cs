using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Partner;

/// <summary>Short cached pull. Dynamic mode never resurrects a withdrawn legacy credit.</summary>
public sealed class TenantBranding : ITenantBranding, IDisposable
{
    private static readonly TenantPartnerDto Hidden = new(null, null);
    private readonly PartnerSettings _settings;
    private readonly ITenantPartner _legacy;
    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _clock;
    private readonly ILogger<TenantBranding> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TenantPartnerDto _cached = Hidden;
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private string? _etag;

    public TenantBranding(IOptions<PartnerSettings> options, ITenantPartner legacy,
        IHttpClientFactory clients, TimeProvider clock, ILogger<TenantBranding> logger)
    {
        _settings = options.Value;
        _legacy = legacy;
        _clients = clients;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TenantPartnerDto> GetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.RuntimeUrl)) return new(_legacy.Name, _legacy.Url);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.GetUtcNow();
            if ((now - _lastAttempt).TotalSeconds >= _settings.RefreshSeconds)
            {
                _lastAttempt = now;
                await RefreshAsync(cancellationToken);
            }
            return (_clock.GetUtcNow() - _lastSuccess).TotalSeconds <= _settings.MaxStaleSeconds
                ? _cached : Hidden;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        var url = $"{_settings.RuntimeUrl.TrimEnd('/')}/{Uri.EscapeDataString(_settings.TenantSlug)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", _etag);
        try
        {
            using var client = _clients.CreateClient(nameof(TenantBranding));
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified && _etag is not null)
            {
                _lastSuccess = _clock.GetUtcNow();
                return;
            }
            // A retired/unknown tenant is an authoritative removal, not an outage.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Store(Hidden, null);
                return;
            }
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<RuntimePayload>(timeout.Token);
            if (payload is null) throw new JsonException("Missing branding payload");
            Store(Project(payload), response.Headers.ETag?.ToString());
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Tenant branding refresh unavailable ({FailureType}); cached attribution expires",
                exception.GetType().Name);
        }
    }

    private void Store(TenantPartnerDto value, string? etag)
    {
        _cached = value;
        _etag = etag;
        _lastSuccess = _clock.GetUtcNow();
    }

    private static TenantPartnerDto Project(RuntimePayload payload)
    {
        var name = payload.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return Hidden;
        if (name.Length > 120) throw new JsonException("Brand name too long");
        var url = Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
        var email = MailAddress.TryCreate(payload.Email, out var address)
            && address.Address == payload.Email ? address.Address : null;
        return new(name, url, email);
    }

    public void Dispose() => _gate.Dispose();

    private sealed record RuntimePayload
    {
        [JsonRequired] public string? Name { get; init; }
        [JsonRequired] public string? Url { get; init; }
        [JsonRequired] public string? Email { get; init; }
    }
}
