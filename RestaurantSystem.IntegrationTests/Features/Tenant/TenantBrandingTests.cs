using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Partner;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

public sealed class TenantBrandingTests
{
    private const string Published = "{\"name\":\"Sofra\",\"url\":\"https://platform.example.test\",\"email\":\"hello@platform.example.test\"}";
    private const string Hidden = "{\"name\":null,\"url\":null,\"email\":null}";

    [Fact]
    public async Task Runtime_lookup_uses_configured_identity_and_shares_a_fresh_cache()
    {
        using var fixture = new Fixture();
        var first = await fixture.Branding.GetAsync(CancellationToken.None);
        var second = await fixture.Branding.GetAsync(CancellationToken.None);
        first.Name.Should().Be("Sofra");
        first.Email.Should().Be("hello@platform.example.test");
        second.Should().Be(first);
        fixture.Handler.Calls.Should().Be(1);
        fixture.Handler.LastUri.Should().Be(new Uri("https://control.example.test/api/public/tenant-branding/tenant"));
    }

    [Fact]
    public async Task Withdrawal_replaces_the_snapshot_without_legacy_fallback()
    {
        using var fixture = new Fixture();
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Handler.Body = Hidden;
        fixture.Clock.Advance(60);
        var withdrawn = await fixture.Branding.GetAsync(CancellationToken.None);
        withdrawn.Name.Should().BeNull();
        withdrawn.Url.Should().BeNull();
        withdrawn.Email.Should().BeNull();
    }

    [Fact]
    public async Task Failure_expires_last_known_branding_and_retries_are_bounded()
    {
        using var fixture = new Fixture();
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Handler.Status = HttpStatusCode.ServiceUnavailable;
        fixture.Clock.Advance(60);
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().Be("Sofra");
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Handler.Calls.Should().Be(2);
        fixture.Clock.Advance(241);
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().BeNull();
    }

    [Fact]
    public async Task Cold_failure_never_publishes_the_legacy_partner()
    {
        using var fixture = new Fixture();
        fixture.Handler.Status = HttpStatusCode.ServiceUnavailable;
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_tenant_withdraws_an_existing_snapshot()
    {
        using var fixture = new Fixture();
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Clock.Advance(60);
        fixture.Handler.Status = HttpStatusCode.NotFound;
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().BeNull();
    }

    [Fact]
    public async Task Etag_revalidation_renews_the_snapshot()
    {
        using var fixture = new Fixture();
        fixture.Handler.Etag = "\"version-1\"";
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Clock.Advance(60);
        fixture.Handler.Status = HttpStatusCode.NotModified;
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().Be("Sofra");
        fixture.Handler.IfNoneMatch.Should().Be("\"version-1\"");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task Malformed_responses_are_failures_instead_of_authoritative_removal(string body)
    {
        using var fixture = new Fixture();
        await fixture.Branding.GetAsync(CancellationToken.None);
        fixture.Clock.Advance(60);
        fixture.Handler.Body = body;
        (await fixture.Branding.GetAsync(CancellationToken.None)).Name.Should().Be("Sofra");
    }

    [Fact]
    public async Task Invalid_links_and_mail_headers_are_not_projected()
    {
        using var fixture = new Fixture();
        fixture.Handler.Body = "{\"name\":\"Studio\",\"url\":\"javascript:alert(1)\",\"email\":\"Bad Name <private@example.test>\"}";
        var value = await fixture.Branding.GetAsync(CancellationToken.None);
        value.Name.Should().Be("Studio");
        value.Url.Should().BeNull();
        value.Email.Should().BeNull();
    }

    private sealed class Fixture : IDisposable
    {
        public Handler Handler { get; } = new();
        public Clock Clock { get; } = new();
        public TenantBranding Branding { get; }
        public Fixture()
        {
            Branding = new TenantBranding(Options.Create(new PartnerSettings
            {
                RuntimeUrl = "https://control.example.test/api/public/tenant-branding",
                TenantSlug = "tenant",
            }), new Legacy(), new Factory(Handler), Clock, NullLogger<TenantBranding>.Instance);
        }
        public void Dispose() { Branding.Dispose(); Handler.Dispose(); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    private sealed class Legacy : ITenantPartner
    {
        public string? Name => "Obsolete partner";
        public string? Url => "https://obsolete.example.test";
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; set; } = Published;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Etag { get; set; }
        public string? IfNoneMatch { get; private set; }
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; LastUri = request.RequestUri;
            IfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            var response = new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
            if (Etag is not null) response.Headers.TryAddWithoutValidation("ETag", Etag);
            return Task.FromResult(response);
        }
    }
}
