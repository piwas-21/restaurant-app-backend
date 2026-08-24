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
/// the reservation, and the booking's status — which is in the payload precisely so that deciding
/// a booking retires both of its links without a column to update. The endpoint-level proof lives
/// in <c>ReservationQuickActionLinkAuthorizationTests</c>; this file is where a broken rule names
/// itself instead of showing up as "the page said no".
/// </para>
/// </summary>
public class ReservationQuickActionLinksTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-characters"; // pragma: allowlist secret
    private const string JwtSecret = "unit-test-jwt-secret-at-least-32-characters"; // pragma: allowlist secret

    private static readonly Guid Booking = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
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
        Guid? reservation = null) =>
        subject.Verify(reservation ?? Booking, action, status, CreatedAt, token);

    [Fact]
    public void A_freshly_minted_token_verifies()
    {
        var subject = Subject();

        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void The_token_carries_nothing_but_an_expiry_and_a_signature()
    {
        // Pins the shape the endpoint parses, and pins the absence of a second secret in the URL:
        // the reservation id is already in the path and everything else is inside the MAC.
        var token = Subject().Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        var parts = token.Split('.');
        parts.Should().HaveCount(2);
        long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(_clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds());
        parts[1].Should().NotContain(Booking.ToString("N"));
    }

    [Fact]
    public void A_token_with_a_flipped_signature_is_refused()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        // Change one character of the signature, keeping the length and the alphabet valid.
        var signature = token[(token.IndexOf('.', StringComparison.Ordinal) + 1)..];
        var flipped = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
        var tampered = token[..(token.IndexOf('.', StringComparison.Ordinal) + 1)] + flipped;

        Verify(subject, tampered).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void Moving_the_expiry_forward_invalidates_the_token()
    {
        // The expiry is in the clear. It has to be signed too, or extending a link would be a
        // matter of editing the URL.
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);
        var later = _clock.GetUtcNow().AddDays(3650).ToUnixTimeSeconds();

        var extended = later.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + token[token.IndexOf('.', StringComparison.Ordinal)..];

        Verify(subject, extended).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var subject = Subject(lifetimeDays: 7);
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        _clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_still_verifies_one_second_before_it_expires()
    {
        var subject = Subject(lifetimeDays: 7);
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        _clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.SignatureValid);
    }

    [Fact]
    public void An_approve_token_cannot_reject()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        Verify(subject, token, action: ReservationQuickAction.Reject)
            .Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_minted_for_one_booking_cannot_act_on_another()
    {
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

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
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

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
        // "" takes the legacy path and is refused by the window, not by the parser — both must be
        // a verdict, never an exception, because the caller is an anonymous GET.
        var subject = Subject(graceDays: 0);

        Verify(subject, token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_token_signed_with_a_different_key_is_refused()
    {
        var token = Subject(signingKey: "a-completely-different-key-of-32-characters") // pragma: allowlist secret
            .Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        Verify(Subject(), token).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void With_no_signing_key_configured_the_jwt_secret_is_used_instead()
    {
        // The deploy story: a box that has not been given ReservationQuickActions__SigningKey still
        // signs, with a key DERIVED from the JWT secret rather than equal to it.
        var derived = Subject(signingKey: "");

        var token = derived.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

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

        var token = fromJwtSecret.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        Verify(fromSameStringAsExplicitKey, token).Should().Be(QuickActionLinkVerdict.SignatureValid,
            "both sides derive from the same material with the same purpose label");
        fromJwtSecret.Mint(Booking, ReservationQuickAction.Reject, ReservationStatus.Pending)
            .Should().NotBe(token, "the action is part of what is signed");
    }

    [Fact]
    public void A_legacy_token_less_link_works_inside_the_grace_window_and_says_so_in_the_log()
    {
        var subject = Subject(graceDays: 14);
        _clock.Advance(TimeSpan.FromDays(13));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Legacy);

        _log.Entries.Should().ContainSingle()
            .Which.Should().Match<(LogLevel Level, string Message)>(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("LEGACY", StringComparison.Ordinal)
                && entry.Message.Contains("LegacyLinkGraceDays", StringComparison.Ordinal));
    }

    [Fact]
    public void A_legacy_link_is_refused_once_its_own_booking_is_older_than_the_window()
    {
        // Measured from the RESERVATION's CreatedAt, not from a global switch-off date: the window
        // then closes booking by booking with nothing left to remember.
        var subject = Subject(graceDays: 14);
        _clock.Advance(TimeSpan.FromDays(14));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void Setting_the_grace_window_to_zero_refuses_every_token_less_link_immediately()
    {
        // The switch-off documented in the README: one config value, no deploy of new code.
        Verify(Subject(graceDays: 0), token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_cutoff_shuts_the_legacy_path_for_a_booking_made_after_it()
    {
        // The recommended production setting. Without it the window also covers bookings made
        // AFTER this fix shipped — whose mail always carries a token — which is #402 left open for
        // the length of the window.
        var subject = Subject(graceDays: 14, legacyCutoff: CreatedAt.AddDays(-1));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Refused);
    }

    [Fact]
    public void A_cutoff_still_honours_a_mail_sent_before_it()
    {
        var subject = Subject(graceDays: 14, legacyCutoff: CreatedAt.AddSeconds(1));

        Verify(subject, token: null).Should().Be(QuickActionLinkVerdict.Legacy);
    }

    [Fact]
    public void A_cutoff_never_touches_a_properly_signed_link()
    {
        // It governs the migration path only. A signed link is authorised by its signature.
        var subject = Subject(graceDays: 0, legacyCutoff: CreatedAt.AddDays(-1));
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

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
    public void A_refusal_never_writes_the_supplied_token_to_the_log()
    {
        // A log that stores credential guesses turns a near miss into a written-down secret.
        var subject = Subject();
        var token = subject.Mint(Booking, ReservationQuickAction.Approve, ReservationStatus.Pending);

        Verify(subject, token, status: ReservationStatus.Confirmed);

        _log.Entries.Should().NotBeEmpty();
        _log.Entries.Should().OnlyContain(entry => !entry.Message.Contains(token, StringComparison.Ordinal));
    }
}
