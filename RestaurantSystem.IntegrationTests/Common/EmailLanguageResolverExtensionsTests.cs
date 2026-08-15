using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The three call-site shapes S5 gives every send path (EMAIL-LOCALISATION-PLAN §5). Pure unit
/// tests: these exist to pin the ARGUMENT each shape does not take, which no integration test can
/// show as directly.
/// </summary>
public class EmailLanguageResolverExtensionsTests
{
    /// <summary>
    /// §6.10. The extensions are the discipline: none of them accepts a request language, so a send
    /// path cannot pick one up by writing the obvious thing. Here a request IS in flight and asks
    /// for German — the header a restaurant's own browser would send while confirming a booking —
    /// and every shape must ignore it.
    /// </summary>
    [Fact]
    public void No_call_site_shape_can_reach_the_request_language()
    {
        var resolver = Resolver(supported: "en,fr,de", header: "de-DE,de;q=0.9");

        resolver.FromRequest().Should().Be("de", "the header really is there to be taken");

        resolver.ForGuest(entityLanguage: null).Should().Be(English);
        resolver.ForAccount(Account(language: null)).Should().Be(English);
        resolver.ForOperator().Should().Be(English);
    }

    [Fact]
    public void A_guests_mail_takes_the_language_frozen_on_the_row()
    {
        var resolver = Resolver(supported: "en,fr,de");

        resolver.ForGuest("fr").Should().Be(CultureInfo.GetCultureInfo("fr"));
        resolver.ForGuest("fr-CH").Should().Be(CultureInfo.GetCultureInfo("fr"),
            "a region-qualified value is canonicalised, not refused");
        resolver.ForGuest(null).Should().Be(English, "every row written before S4 has none");
        resolver.ForGuest("klingon").Should().Be(English);
        resolver.ForGuest("it").Should().Be(English, "a language this tenant does not sell in is absent");
    }

    [Fact]
    public void An_accounts_mail_takes_the_accounts_own_preference()
    {
        var resolver = Resolver(supported: "en,fr,de");

        resolver.ForAccount(Account("fr")).Should().Be(CultureInfo.GetCultureInfo("fr"));
        resolver.ForAccount(Account(language: null)).Should().Be(English,
            "an account that never expressed one falls through to the tenant");
    }

    /// <summary>
    /// The operator alerts follow the tenant even on a tenant that does not sell in English at all
    /// — the leg that would be hidden if every fixture defaulted to <c>en</c>.
    /// </summary>
    [Fact]
    public void The_operator_reads_its_own_language_whatever_the_guest_asked_for()
    {
        var resolver = Resolver(supported: "fr,de", defaultLanguage: "de", header: "fr");

        resolver.ForOperator().Should().Be(CultureInfo.GetCultureInfo("de"));
        resolver.ForGuest("fr").Should().Be(CultureInfo.GetCultureInfo("fr"),
            "the same resolver still answers the guest in theirs");
    }

    private static ApplicationUser Account(string? language) => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Role = UserRole.Customer,
        CreatedBy = "test",
        RefreshToken = "not-a-token",   // pragma: allowlist secret — a required member, unused here
        PreferredLanguage = language
    };

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static EmailLanguageResolver Resolver(
        string? supported = null, string? defaultLanguage = null, string? header = null)
    {
        HttpContext? context = null;

        if (header is not null)
        {
            context = new DefaultHttpContext();
            context.Request.Headers.AcceptLanguage = header;
        }

        return new EmailLanguageResolver(
            Options.Create(new LocalizationSettings
            {
                SupportedLanguages = supported ?? string.Empty,
                DefaultLanguage = defaultLanguage ?? string.Empty
            }),
            new FixedAccessor(context),
            NullLogger<EmailLanguageResolver>.Instance);
    }

    /// <inheritdoc cref="EmailLanguageResolverTests"/>
    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
