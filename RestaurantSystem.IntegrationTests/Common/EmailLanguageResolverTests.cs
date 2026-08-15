using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The §1 resolution chain (EMAIL-LOCALISATION-PLAN S3). Pure unit tests with a hand-built
/// <see cref="HttpContext"/>: the whole point of this design is that a language is a value that is
/// passed, so nothing here needs a host, and a test that needed one would be testing the wrong
/// thing.
/// </summary>
public class EmailLanguageResolverTests
{
    /// <summary>
    /// The legacy-RUMI case, and the state every instance is in until S9 wires the deploy keys:
    /// no configuration at all. Reading that as "no languages" would leave nothing to write a mail
    /// in; reading it as "all ten" keeps today's behaviour exactly.
    /// </summary>
    [Fact]
    public void An_unconfigured_instance_supports_all_ten_languages_and_defaults_to_English()
    {
        var resolver = Resolver();

        resolver.SupportedLanguages.Should().Equal(LanguageCode.Supported, "the interface promises configured order");
        resolver.TenantDefault.Should().Be("en");
        resolver.Resolve(null, null, null).Should().Be("en");
    }

    [Fact]
    public void A_configured_list_narrows_what_can_be_resolved()
    {
        var resolver = Resolver(supported: "fr, de");

        resolver.SupportedLanguages.Should().Equal("fr", "de");
        resolver.Resolve(entityLanguage: "it", userLanguage: null, requestLanguage: null).Should().Be("fr",
            "a language this tenant does not sell in is absent, not an error");
    }

    /// <summary>
    /// §1 rank 4 is "the first entry of the registry's languages list", not "en". A tenant that
    /// sells only in French must not receive its own operator alerts in English because nobody
    /// set the second key.
    /// </summary>
    [Fact]
    public void The_tenant_default_falls_to_the_first_configured_language_not_to_English()
    {
        Resolver(supported: "fr,de").TenantDefault.Should().Be("fr");
        Resolver(supported: "fr,de", defaultLanguage: "de").TenantDefault.Should().Be("de");
        Resolver(supported: "fr,de", defaultLanguage: "it").TenantDefault.Should().Be("fr",
            "a default outside the tenant's own list is a misconfiguration, not a language");
        Resolver(defaultLanguage: "fr").TenantDefault.Should().Be("fr",
            "a default is honoured on an instance with no configured list at all");
    }

    [Fact]
    public void A_configuration_holding_no_usable_code_is_treated_as_unconfigured()
    {
        var resolver = Resolver(supported: "klingon, , 42");

        resolver.SupportedLanguages.Should().Equal(LanguageCode.Supported);
        resolver.TenantDefault.Should().Be("en");
    }

    [Fact]
    public void The_chain_prefers_the_entity_then_the_user_then_the_request_then_the_tenant()
    {
        var resolver = Resolver(supported: "de,fr,nl,en", header: "nl");
        var request = resolver.FromRequest();

        resolver.Resolve("fr", "de", request).Should().Be("fr");   // rank 1
        resolver.Resolve(null, "en", request).Should().Be("en");   // rank 2
        resolver.Resolve(null, null, request).Should().Be("nl");   // rank 3
        // rank 4, on a list whose first entry is NOT `en`, so this leg cannot be satisfied by the
        // terminal fallback pretending to be the tenant default.
        resolver.Resolve(null, null, null).Should().Be("de");
    }

    /// <summary>
    /// The frozen-language rule (§6.5): an order keeps the language it was placed in even after
    /// the guest changes their profile, so a resend matches the receipt they already have.
    /// </summary>
    [Fact]
    public void An_entitys_frozen_language_outranks_a_later_profile_change()
    {
        var resolver = Resolver(header: "de");

        resolver.Resolve("fr", "nl", resolver.FromRequest()).Should().Be("fr");
    }

    [Fact]
    public void An_unsupported_value_at_any_rank_falls_through_rather_than_failing()
    {
        var resolver = Resolver(supported: "en,fr", header: "de");

        resolver.Resolve("klingon", "zz", resolver.FromRequest()).Should().Be("en",
            "the header asks for a language this tenant does not sell in either");
    }

    /// <summary>
    /// The header is a weighted list from an attacker-controlled, anonymous request. Three rules a
    /// naive split gets wrong: q ranks the entries, q=0 means "explicitly NOT this one", and the
    /// wildcard is not a language.
    /// </summary>
    [Theory]
    [InlineData("fr-CH,fr;q=0.9,en;q=0.8", "fr")]
    [InlineData("fr,en;q=0.9", "fr")]                 // region-less: the form S2's helper refuses whole
    [InlineData("en;q=0.3,de;q=0.9", "de")]           // weight, not order, decides
    [InlineData("de;q=0,fr;q=0.1", "fr")]             // q=0 is a refusal, not a low rank
    [InlineData("*", null)]                            // wildcard selects nothing
    [InlineData("it,ru", null)]                        // nothing this tenant sells in
    [InlineData("de;q=notanumber,fr", "fr")]           // malformed q is dropped, never thrown
    [InlineData("de;q=Infinity,fr", "fr")]             // RFC 7231 bounds q to [0,1]
    [InlineData("de;q=1e9,fr", "fr")]
    [InlineData("de;q=-0.5,fr", "fr")]
    [InlineData("", null)]
    [InlineData(",,,", null)]
    public void The_request_language_is_the_highest_quality_supported_entry(string header, string? expected) =>
        Resolver(supported: "en,fr,de", header: header).FromRequest().Should().Be(expected);

    /// <summary>
    /// The property the whole API shape exists for: rank 3 is an ARGUMENT, so a send path that is
    /// not the guest's own request cannot pick up a language it has no business using — the
    /// reservation quick-action links clicked in the restaurant's browser, an admin status change,
    /// and above all the operator alerts M14/M15, which §1 says must follow the tenant and never
    /// the guest. Every pre-S4 row has a null language, so an implicit rank 3 would have made this
    /// the common case rather than the edge one.
    /// </summary>
    [Fact]
    public void A_send_that_is_not_the_guests_own_request_gets_the_tenant_language()
    {
        var resolver = Resolver(supported: "de,fr", header: "fr");

        resolver.Resolve(null, null, null).Should().Be("de", "nothing passed rank 3");
        resolver.FromRequest().Should().Be("fr", "the request really does ask for French");
    }

    /// <summary>
    /// Deliberate and documented (plan §6.9): the tenant's supported set is its UI locale list, so
    /// a guest can be captured in a language no <c>.resx</c> exists for yet. The mail falls back to
    /// English copy (S1) rather than failing, and starts rendering in that language when S7/S8+
    /// ship it. Storing the guest's actual choice is what makes that possible; the alternative —
    /// storing English because nobody has translated yet — loses the fact forever.
    /// </summary>
    [Fact]
    public void A_language_with_no_translation_yet_is_still_resolved()
    {
        Resolver().Resolve("ar", null, null).Should().Be("ar");
    }

    [Fact]
    public void There_is_no_request_language_outside_a_request() =>
        Resolver(supported: "en,fr").FromRequest().Should().BeNull(
            "the detached order tasks, the Stripe webhook and every BackgroundService run here");

    /// <summary>
    /// §6.1, the failure this whole design exists to prevent: an ambient culture is silently unset
    /// on the paths that send most mail. If the resolver ever consulted it, this test would pass a
    /// French answer back from a request that asked for nothing.
    /// </summary>
    [Fact]
    public void The_resolver_never_reads_the_ambient_culture()
    {
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fr-CH");

            Resolver(supported: "en,fr").Resolve(null, null, null).Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static EmailLanguageResolver Resolver(
        string? supported = null, string? defaultLanguage = null, string? header = null)
    {
        var settings = new LocalizationSettings
        {
            SupportedLanguages = supported ?? string.Empty,
            DefaultLanguage = defaultLanguage ?? string.Empty
        };

        HttpContext? context = null;

        if (header is not null)
        {
            context = new DefaultHttpContext();
            context.Request.Headers.AcceptLanguage = header;
        }

        return new EmailLanguageResolver(
            Options.Create(settings), new FixedAccessor(context), NullLogger<EmailLanguageResolver>.Instance);
    }

    /// <summary>
    /// Deliberately NOT the framework's <see cref="HttpContextAccessor"/>: its backing field is a
    /// static <c>AsyncLocal</c>, so a context set for one resolver in this class leaked into the
    /// next one and a "no request at all" case silently ran with the previous test's header.
    /// </summary>
    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
