using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common;

namespace RestaurantSystem.IntegrationTests.Common;

// GetClientIp reads the LAST X-Forwarded-For hop (Caddy-appended, trustworthy);
// the first hop is client-supplied and spoofable (sofra #30). No DB — a plain
// DefaultHttpContext is enough, so this needs no Testcontainers fixture.
public class ClientIpExtensionsTests
{
    private static DefaultHttpContext Ctx(string? xff, string? remoteIp = "10.0.0.1")
    {
        var ctx = new DefaultHttpContext();
        if (xff is not null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        if (remoteIp is not null) ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return ctx;
    }

    [Fact]
    public void Takes_the_last_hop_from_a_multi_hop_header()
    {
        Ctx("203.0.113.9, 10.0.0.1, 10.0.0.2").GetClientIp().Should().Be("10.0.0.2");
    }

    [Fact]
    public void Ignores_a_client_spoofed_leading_hop()
    {
        // Attacker sends `X-Forwarded-For: 1.2.3.4`; Caddy appends the real IP on the
        // right, so keying on the last hop makes the spoof a no-op.
        Ctx("1.2.3.4, 203.0.113.9").GetClientIp().Should().Be("203.0.113.9");
    }

    [Fact]
    public void Trims_whitespace_around_the_last_hop()
    {
        Ctx("10.0.0.1,  198.51.100.4  ").GetClientIp().Should().Be("198.51.100.4");
    }

    [Fact]
    public void Returns_the_sole_hop_for_a_single_value_header()
    {
        Ctx("203.0.113.9").GetClientIp().Should().Be("203.0.113.9");
    }

    [Fact]
    public void Falls_back_to_the_connection_ip_when_the_header_is_absent()
    {
        Ctx(xff: null, remoteIp: "192.0.2.7").GetClientIp().Should().Be("192.0.2.7");
    }

    [Fact]
    public void Falls_back_to_the_connection_ip_for_a_blank_header()
    {
        Ctx("   ", remoteIp: "192.0.2.8").GetClientIp().Should().Be("192.0.2.8");
    }

    [Fact]
    public void Returns_unknown_when_no_header_and_no_connection_ip()
    {
        Ctx(xff: null, remoteIp: null).GetClientIp().Should().Be("unknown");
    }
}
