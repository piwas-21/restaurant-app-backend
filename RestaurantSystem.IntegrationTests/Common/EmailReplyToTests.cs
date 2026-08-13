using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// <see cref="EmailSettings.ReplyToEmail"/> — added for tenants on the SHARED platform
/// sending domain, where <c>FromEmail</c> is <c>&lt;slug&gt;@send.sofrapiwas.com</c>, an
/// address nobody reads. Without a Reply-To a guest answering an order confirmation is
/// talking to a black hole.
///
/// The behaviour worth pinning is the ABSENT case, not the present one: an unconfigured
/// Reply-To must omit the JSON property entirely. Serializing <c>"reply_to": ""</c> would
/// be a malformed header rather than an absent one, and it only shows up in a delivered
/// message — no build, type check or status code would catch it.
/// </summary>
public class EmailReplyToTests
{
    private static EmailSettings ResendSettings(string replyTo) => new()
    {
        Provider = "Resend",
        ResendApiKey = "re_test", // pragma: allowlist secret — fixture value; no request leaves the process
        FromEmail = "kebabhouse@send.sofrapiwas.com",
        FromName = "Kebab House",
        AdminEmail = "owner@kebabhouse.ch",
        ReplyToEmail = replyTo,
        FrontendBaseUrl = "https://kebabhouse.sofrapiwas.com",
        BackendBaseUrl = "https://kebabhouse.sofrapiwas.com",
    };

    /// <summary>Captures the outgoing request body instead of reaching Resend.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task<JsonElement> SendAndCaptureAsync(string replyTo)
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") };
        var sender = new ResendEmailSender(http, Options.Create(ResendSettings(replyTo)));

        await sender.SendAsync(new OutgoingEmail("guest@example.com", "Your order", "<p>Thanks</p>"));

        return JsonDocument.Parse(handler.Body).RootElement.Clone();
    }

    [Fact]
    public async Task Resend_OmitsReplyToProperty_WhenNotConfigured()
    {
        var payload = await SendAndCaptureAsync(string.Empty);

        // Absent, NOT present-and-empty.
        payload.TryGetProperty("reply_to", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Resend_SendsReplyTo_WhenConfigured()
    {
        var payload = await SendAndCaptureAsync("info@kebabhouse.ch");

        payload.GetProperty("reply_to").GetString().Should().Be("info@kebabhouse.ch");
        // The From must stay the platform domain — Reply-To redirects replies, it does not
        // change who the mail is sent as. Sending AS an unverified tenant domain would 403.
        payload.GetProperty("from").GetString().Should().Be("Kebab House <kebabhouse@send.sofrapiwas.com>");
    }

    [Fact]
    public void Validate_Passes_WhenReplyToIsEmpty()
    {
        var act = () => ResendSettings(string.Empty).Validate();

        act.Should().NotThrow("an unset Reply-To is the default and must stay optional");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@ats.ch")]
    [InlineData("@no-local-part.ch")]
    [InlineData("no-domain@")]
    public void Validate_Throws_WhenReplyToIsMalformed(string bad)
    {
        // Caught at startup rather than at send time: SmtpEmailSender would otherwise throw
        // FormatException on EVERY send and Resend would 422 the whole request, turning a
        // one-character typo into a total mail outage found by a customer.
        var act = () => ResendSettings(bad).Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Reply-To*");
    }

    /// <summary>
    /// States the guard's real reach so nobody mistakes it for deliverability validation.
    /// <see cref="System.ComponentModel.DataAnnotations.EmailAddressAttribute"/> is
    /// deliberately lenient — a dotless domain is legal (intranet addresses), so
    /// <c>owner@localhost</c> passes. It catches typos of SHAPE, not of destination; a
    /// well-formed address at a domain that does not exist still bounces at send time.
    /// This is asserted rather than left implicit because the earlier version of this test
    /// assumed the opposite and failed.
    /// </summary>
    [Theory]
    [InlineData("owner@localhost")]
    [InlineData("owner@tld")]
    public void Validate_Accepts_DotlessDomains_TheCheckIsShapeOnly(string lenient)
    {
        var act = () => ResendSettings(lenient).Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Passes_WhenReplyToIsValid()
    {
        var act = () => ResendSettings("info@kebabhouse.ch").Validate();

        act.Should().NotThrow();
    }
}
