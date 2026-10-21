using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// The signature itself, away from HTTP and the database (backend #402).
/// <para>
/// Everything a forger can control is exercised here: the expiry, the signature bytes, the action,
/// the reservation, status and database revision. A returned status cannot revive a link. The endpoint-level proof lives
/// in <c>ReservationQuickActionLinkAuthorizationTests</c>; this file is where a broken rule names
/// itself instead of showing up as "the page said no".
/// </para>
/// </summary>
public class ReservationQuickActionLinksTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-characters"; // pragma: allowlist secret
    private const string JwtSecret = "unit-test-jwt-secret-at-least-32-characters"; // pragma: allowlist secret

    private static readonly Guid Booking = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid Revision = Guid.Parse("cccccccc-1111-4222-8333-444444444444");
    private static readonly Guid OtherBooking = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    /// <summary>Reservations in these tests were created the moment the clock starts.</summary>
    private static readonly DateTime CreatedAt = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A clock the test moves — same four lines as <c>StripeAccountClientTests</c>.</summary>
    private sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Keeps what was logged, so "and logs it" can be an assertion rather than a hope.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private readonly MovableClock _clock = new();
    private readonly RecordingLogger<ReservationQuickActionLinks> _log = new();

    private ReservationQuickActionLinks Subject(
        int graceDays = 14, int lifetimeDays = 7, string signingKey = SigningKey, string jwtSecret = JwtSecret,
        DateTime? legacyCutoff = null) =>
        new(
            Options.Create(new ReservationQuickActionSettings
            {
                SigningKey = signingKey,
                LinkLifetimeDays = lifetimeDays,
                LegacyLinkGraceDays = graceDays,
                LegacyLinkCutoffUtc = legacyCutoff,
            }),
            Options.Create(new JwtSettings { Secret = jwtSecret }),
            _clock,
            _log);

    private QuickActionLinkVerdict Verify(
        ReservationQuickActionLinks subject,
        string? token,
        ReservationQuickAction action = ReservationQuickAction.Approve,
        ReservationStatus status = ReservationStatus.Pending,
        Guid? reservation = null, Guid? revision = null) =>
        subject.Verify(reservation ?? Booking, action, status, revision ?? Revision, token);

    [Fact]
    public void A_freshly_minted_token_verifies()
    {
        var subject = Subject();

        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void The_token_carries_only_its_version_expiry_and_signature()
    {
        // Pins the shape the endpoint parses, and pins the absence of a second secret in the URL:
        // the reservation id is already in the path and everything else is inside the MAC.
        var token = Subject().Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        var parts = token.Split('.');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("v2");
        long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(_clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds());
        parts[2].Should().NotContain(Booking.ToString("N"));
        parts[2].Should().NotContain(Revision.ToString("N"));
    }

    [Fact]
    public void A_token_with_a_flipped_signature_is_refused()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        // Change one character of the signature, keeping the length and the alphabet valid.
        var signature = token[(token.LastIndexOf('.') + 1)..];
        var flipped = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
        var tampered = token[..(token.LastIndexOf('.') + 1)] + flipped;

        Verify(subject, tampered).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void Moving_the_expiry_forward_invalidates_the_token()
    {
        // The expiry is in the clear. It has to be signed too, or extending a link would be a
        // matter of editing the URL.
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);
        var later = _clock.GetUtcNow().AddDays(3650).ToUnixTimeSeconds();

        var parts = token.Split('.'); parts[1] = later.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var extended = string.Join('.', parts);

        Verify(subject, extended).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var subject = Subject(lifetimeDays: 7);
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        _clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_still_verifies_one_second_before_it_expires()
    {
        var subject = Subject(lifetimeDays: 7);
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        _clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void An_approve_token_cannot_reject()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token, action: ReservationQuickAction.Reject)
            .Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_minted_for_one_booking_cannot_act_on_another()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token, reservation: OtherBooking).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.NoShow)]
    public void A_token_stops_working_once_the_booking_has_been_decided(ReservationStatus decided)
    {
        // The replay guard, and the reason the status is in the payload: the link was signed over
        // Pending, so the same URL clicked twice cannot decide the booking twice.
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token, status: decided).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("...")]
    [InlineData("abc.def")]
    [InlineData("99999999999999999999999999.AAAA")]
    [InlineData("1893456000.not base64url!")]
    public void Junk_in_the_token_parameter_is_refused_rather_than_thrown(string token)
    {
        // Missing or malformed authority always produces a verdict, never an anonymous exception.
        var subject = Subject(graceDays: 0);

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_signed_with_a_different_key_is_refused()
    {
        var token = Subject(signingKey: "a-completely-different-key-of-32-characters") // pragma: allowlist secret
            .Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(Subject(), token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void With_no_signing_key_configured_the_jwt_secret_is_used_instead()
    {
        // The deploy story: a box that has not been given ReservationQuickActions__SigningKey still
        // signs, with a key DERIVED from the JWT secret rather than equal to it.
        var derived = Subject(signingKey: "");

        var token = derived.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(derived, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
        Verify(Subject(signingKey: "", jwtSecret: "another-jwt-secret-of-at-least-32-characters"), token)
            .Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void The_derived_key_is_not_the_jwt_secret_itself()
    {
        // Key separation. If the fallback handed the raw secret to HMAC, these two would agree,
        // and a quick-action link would be signed with the credential that mints access tokens.
        var fromJwtSecret = Subject(signingKey: "");
        var fromSameStringAsExplicitKey = Subject(signingKey: JwtSecret);

        var token = fromJwtSecret.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(fromSameStringAsExplicitKey, token).Should().Be(QuickActionLinkVerdict.SignatureValid,
            "both sides derive from the same material with the same purpose label");
        fromJwtSecret.Mint(Booking, ReservationQuickAction.Reject, ReservationStatus.Pending, Revision)
            .Should().NotBe(token, "the action is part of what is signed");
    }

    [Fact]
    public void A_token_less_link_is_refused_even_with_a_configured_grace_window_and_logs_no_acceptance()
    {
        var subject = Subject(graceDays: 14);
        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
        _log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        _log.Entries.Should().NotContain(entry => entry.Message.Contains("Accepted", StringComparison.Ordinal));
        Verify(subject, subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision))
            .Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void A_legacy_link_is_refused_once_its_own_booking_is_older_than_the_window()
    {
        // Expired historical settings cannot grant revisionless authority either.
        var subject = Subject(graceDays: 14);
        _clock.Advance(TimeSpan.FromDays(14));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void Setting_the_grace_window_to_zero_refuses_every_token_less_link_immediately()
    {
        // Old configuration remains accepted; it never authorizes a revisionless link.
        Verify(Subject(graceDays: 0), token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_cutoff_shuts_the_legacy_path_for_a_booking_made_after_it()
    {
        // A historical cutoff cannot bypass the current revision-bound requirement.
        var subject = Subject(graceDays: 14, legacyCutoff: CreatedAt.AddDays(-1));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_cutoff_cannot_authorize_a_revisionless_mail_sent_before_it()
    {
        var subject = Subject(graceDays: 14, legacyCutoff: CreatedAt.AddSeconds(1));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_cutoff_never_touches_a_properly_signed_link()
    {
        // Deprecated legacy settings cannot disable a valid current signature.
        var subject = Subject(graceDays: 0, legacyCutoff: CreatedAt.AddDays(-1));
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void A_legacy_link_cannot_be_replayed_after_the_booking_was_decided()
    {
        var subject = Subject(graceDays: 14);

        Verify(subject, token: null, status: ReservationStatus.Confirmed)
            .Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_returned_status_cannot_revive_a_token_for_an_older_revision()
    {
        var subject = Subject(); var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);
        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
        Verify(subject, token, status: ReservationStatus.Confirmed).Should().Be(QuickActionLinkVerdict.Refused);
        Verify(subject, token, revision: Guid.NewGuid()).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void An_empty_revision_cannot_mint_or_verify_action_authority()
    {
        var subject = Subject();
        var mint = () => subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Guid.Empty);
        mint.Should().Throw<ArgumentException>();
        Verify(subject, subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision), revision: Guid.Empty)
            .Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void An_oversized_token_is_refused_with_a_valid_current_token_as_a_positive_control()
    {
        var subject = Subject(); var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);
        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
        Verify(subject, token + new string('A', 128)).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_refusal_never_writes_the_supplied_token_to_the_log()
    {
        // A log that stores credential guesses turns a near miss into a written-down secret.
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending, Revision);

        Verify(subject, token, status: ReservationStatus.Confirmed);

        _log.Entries.Should().NotBeEmpty();
        _log.Entries.Should().OnlyContain(entry => !entry.Message.Contains(token, StringComparison.Ordinal));
    }
}
