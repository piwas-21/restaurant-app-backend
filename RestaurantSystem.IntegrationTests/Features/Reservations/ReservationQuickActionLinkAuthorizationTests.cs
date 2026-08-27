using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// The two reservation email links, end to end (backend #402).
/// <para>
/// They stay <c>[AllowAnonymous]</c> — they are opened from a mail client that carries no session —
/// so the whole of the authorisation is the <c>?token=</c>. Before it existed, the bare reservation
/// id WAS the authorisation, and <c>POST /api/Reservations</c> is anonymous and returns that id to
/// whoever made the booking: a guest could approve their own table and the restaurant would be
/// told it had agreed. The negative cases below are the ones that fail if the check is removed.
/// </para>
/// <para>
/// Every refusal must land on the same page as an unknown id, or the route becomes a way to ask
/// which reservation ids are real — hence the "same page" assertions rather than status codes.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class ReservationQuickActionLinkAuthorizationTests : IntegrationTestBase
{
    private static readonly Guid TableId = Guid.Parse("eeeeeeee-0402-0000-0000-000000000001");

    /// <summary>Text unique to the refusal page. One page for expired, tampered, missing and unknown.</summary>
    private const string RefusalMarker = "This link can no longer be used";

    public ReservationQuickActionLinkAuthorizationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    /// <summary>
    /// The shape a box SHOULD be released with: a legacy cutoff in the past, so a booking made
    /// today can never take the token-less path however wide the grace window is. The window
    /// itself is proven in <c>ReservationQuickActionLegacyLinkTests</c>, which leaves the cutoff
    /// unset.
    /// </summary>
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.Configure<ReservationQuickActionSettings>(o => o.LegacyLinkCutoffUtc = DateTime.UtcNow.AddDays(-7));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Tables.Add(new Table { Id = TableId, TableNumber = "T-402", MaxGuests = 4, CreatedBy = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_signed_approve_link_confirms_the_booking()
    {
        var id = await SeedReservationAsync();
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-approve", MintFor(id, ReservationQuickAction.Approve));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Reservation Approved");
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task A_signed_reject_link_cancels_the_booking()
    {
        var id = await SeedReservationAsync();
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-reject", MintFor(id, ReservationQuickAction.Reject));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Reservation Rejected");
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task The_guest_who_made_the_booking_cannot_approve_it_with_the_id_alone()
    {
        // #402 in one test: the id is not a secret — POST /api/Reservations is anonymous and
        // returns it to whoever made the booking.
        var id = await SeedReservationAsync();
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-approve", token: null);

        await ShouldBeRefusedAsync(response);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending, "the restaurant has not decided yet");
    }

    [Fact]
    public async Task A_tampered_token_is_refused()
    {
        var id = await SeedReservationAsync();
        var token = MintFor(id, ReservationQuickAction.Approve);
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-approve", token[..^2] + (token.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA"));

        await ShouldBeRefusedAsync(response);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task An_approve_token_cannot_be_replayed_on_the_reject_route()
    {
        var id = await SeedReservationAsync();
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-reject", MintFor(id, ReservationQuickAction.Approve));

        await ShouldBeRefusedAsync(response);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var id = await SeedReservationAsync();
        // Correctly signed with the host's own key, but minted by a clock 30 days in the past, so
        // it carries an expiry that has already gone by — exactly the link that has sat in an inbox.
        var token = MintFor(id, ReservationQuickAction.Approve, mintedAt: DateTimeOffset.UtcNow.AddDays(-30));
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(id, "quick-approve", token);

        await ShouldBeRefusedAsync(response);
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task The_same_signed_link_cannot_decide_the_booking_twice()
    {
        var id = await SeedReservationAsync();
        var token = MintFor(id, ReservationQuickAction.Approve);
        AuthenticateAsAnonymous();

        (await GetLinkAsync(id, "quick-approve", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeRefusedAsync(await GetLinkAsync(id, "quick-approve", token));
    }

    [Fact]
    public async Task Approving_a_booking_also_retires_the_reject_button_in_the_same_mail()
    {
        // Both tokens are signed over the status the mail was written at. Once the booking moves
        // off Pending the whole mail is spent — the restaurant changes its mind in the dashboard,
        // where there is a session to hold responsible.
        var id = await SeedReservationAsync();
        var rejectToken = MintFor(id, ReservationQuickAction.Reject);
        AuthenticateAsAnonymous();

        await GetLinkAsync(id, "quick-approve", MintFor(id, ReservationQuickAction.Approve));

        await ShouldBeRefusedAsync(await GetLinkAsync(id, "quick-reject", rejectToken));
        (await StatusOfAsync(id)).Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task An_unknown_reservation_answers_exactly_what_a_bad_token_answers()
    {
        // The anti-oracle assertion. If these two bodies ever diverge, the route becomes a way to
        // ask which reservation ids exist.
        var real = await SeedReservationAsync();
        AuthenticateAsAnonymous();

        var unknown = await GetLinkAsync(Guid.NewGuid(), "quick-approve", MintFor(real, ReservationQuickAction.Approve));
        var badToken = await GetLinkAsync(real, "quick-approve", "1893456000.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        unknown.StatusCode.Should().Be(badToken.StatusCode);
        (await unknown.Content.ReadAsStringAsync())
            .Should().Be(await badToken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_refused_link_renders_a_page_and_never_a_stack_trace()
    {
        AuthenticateAsAnonymous();

        var response = await GetLinkAsync(Guid.NewGuid(), "quick-approve", "obvious-nonsense");

        var body = await response.Content.ReadAsStringAsync();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        body.Should().Contain(RefusalMarker);
        body.Should().NotContain("Exception", "an anonymous caller must never be shown a stack trace");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> GetLinkAsync(Guid id, string action, string? token) =>
        Client.GetAsync(token is null
            ? $"/api/reservations/{id}/{action}"
            : $"/api/reservations/{id}/{action}?token={Uri.EscapeDataString(token)}");

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        // OK-with-a-page, not a 4xx: the reader is a restaurant owner in a mail client, and the
        // status code is not what they see. What matters is that nothing was decided.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(RefusalMarker);
    }

    /// <summary>
    /// A token the host itself would accept. <paramref name="mintedAt"/> lets a test mint one with
    /// the real key but a clock in the past, which is the only honest way to produce an EXPIRED
    /// token without waiting a week or exposing an "expires at" parameter in production code.
    /// </summary>
    private string MintFor(Guid id, ReservationQuickAction action, DateTimeOffset? mintedAt = null)
    {
        if (mintedAt is null)
        {
            return Factory.Services.GetRequiredService<IReservationQuickActionLinks>()
                .Mint(id, action, ReservationStatus.Pending);
        }

        var links = new ReservationQuickActionLinks(
            Factory.Services.GetRequiredService<IOptions<ReservationQuickActionSettings>>(),
            Factory.Services.GetRequiredService<IOptions<JwtSettings>>(),
            new FrozenClock(mintedAt.Value),
            NullLogger<ReservationQuickActionLinks>.Instance);

        return links.Mint(id, action, ReservationStatus.Pending);
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<Guid> SeedReservationAsync(DateTime? createdAt = null)
    {
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Reservations.Add(new Reservation
        {
            Id = id,
            CustomerId = null,
            CustomerName = "Ada Lovelace",
            CustomerEmail = "ada@example.com",
            CustomerPhone = "+41791112233",
            TableId = TableId,
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            NumberOfGuests = 2,
            Status = ReservationStatus.Pending,
            // Anchor of the legacy grace window. Left at "now" unless a test is about that window;
            // ApplicationDbContext only stamps it when it is still default.
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();

        return id;
    }

    private async Task<ReservationStatus> StatusOfAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id)).Status;
    }
}
