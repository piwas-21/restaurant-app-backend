using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Utilities;
using Sentry;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class TableGuestTelemetryPrivacyTests
{
    [Fact]
    public void Error_report_headers_omit_capabilities_in_every_casing_and_keep_diagnostic_headers()
    {
        var captured = new SentryEvent(new Exception("Diagnostic failure"));
        captured.Request.Headers["x-table-participant"] = "participant-secret";
        captured.Request.Headers["X-TABLE-PARTICIPANT"] = "another-participant-secret";
        captured.Request.Headers["x-SeSsIoN-Id"] = "basket-capability";
        captured.Request.Headers["Accept-Language"] = "fr";
        captured.Request.Headers["Content-Type"] = "application/json";

        var filtered = TableGuestTelemetryPrivacy.Filter(captured);
        filtered.Should().BeSameAs(captured);
        var outgoingHeaders = JsonSerializer.Serialize(filtered.Request.Headers);
        outgoingHeaders.Should().NotContain("participant-secret").And.NotContain("basket-capability");
        filtered.Request.Headers["Accept-Language"].Should().Be("fr");
        filtered.Request.Headers["Content-Type"].Should().Be("application/json");
    }

    [Fact]
    public void Reports_without_request_capabilities_are_preserved()
    {
        var captured = new SentryEvent(new Exception("Diagnostic failure"));

        TableGuestTelemetryPrivacy.Filter(captured).Should().BeSameAs(captured);
        captured.Exception!.Message.Should().Be("Diagnostic failure");
        captured.Request.Headers.Should().BeEmpty();
    }
}
