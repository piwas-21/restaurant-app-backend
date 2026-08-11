using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// What a validation failure looks like ON THE WIRE, over a real host (#291).
///
/// <c>UpdateStaffValidationContractTests</c> pins the two ends host-free — what
/// <c>ValidationBehavior</c> throws and what <c>ApiResponse.Failure</c> makes of it — and says so
/// explicitly, because the hop between them is the middleware and it was "read, not executed".
/// That hop is exactly where #291 lives: <c>ExceptionHandlingMiddleware</c> is what decides whether
/// the per-rule reasons reach <c>errors[]</c> or are replaced by a single detail string. So these
/// go through HTTP.
///
/// What these do NOT cover, stated because a first draft claimed they did: the Development branch
/// of `detail` (`_environment.IsDevelopment() ? exception.ToString() : message`). The test host
/// runs `UseEnvironment("Test")` (`TestWebApplicationFactory.cs:39`) and `IsDevelopment()` compares
/// against the literal "Development", so that branch is unreachable from any integration test — an
/// assertion here that no entry looks like a stack trace could never have failed. That branch has
/// no coverage anywhere, before or after #291, and both deployed environments pin Production.
///
/// `POST /api/user/register/customer` is the vehicle: anonymous, and its validator breaks several
/// rules at once on a single bad payload. Raw JSON so the request is exactly what a client sends.
/// </summary>
public class ValidationErrorContractTests : IntegrationTestBase
{
    public ValidationErrorContractTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>Reads <c>errors[]</c> and <c>message</c> off the envelope, whatever else it carries.</summary>
    private static async Task<(List<string> Errors, string Message)> ReadFailureAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var errors = root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        return (errors, root.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task AWeakPassword_ServesOneErrorsEntryPerBrokenRule()
    {
        var response = await Client.PostAsync("/api/user/register/customer", Json("""
            {
              "firstName": "Ada",
              "lastName": "Lovelace",
              "email": "ada.validation.contract@example.com",
              "password": "weak",
              "confirmPassword": "weak"
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (errors, message) = await ReadFailureAsync(response);

        // "weak" breaks four of the five strength rules and satisfies lowercase. Asserted exactly
        // rather than as "several", so a change that collapses or reorders them cannot slip past.
        errors.Should().BeEquivalentTo(new[]
        {
            "Password must be at least 8 characters long",
            "Password must contain at least one uppercase letter",
            "Password must contain at least one digit",
            "Password must contain at least one special character",
        });

        // The half that makes #291 additive: the joined sentence is still there, on `message`.
        message.Should().Contain("; ");
        message.Should().Contain("Password must be at least 8 characters long");

        // No entry may itself be a joined blob — that is the defect, and the reason the frontend's
        // first matching field used to claim every rule at once.
        errors.Should().OnlyContain(e => !e.Contains("; ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RulesFromDifferentFields_EachGetTheirOwnEntry()
    {
        // The shape #291 was actually reported for: a registration failing on BOTH password and
        // email. With one joined blob the client's first matching pattern claimed the whole string
        // and the email field stayed silent.
        var response = await Client.PostAsync("/api/user/register/customer", Json("""
            {
              "firstName": "Ada",
              "lastName": "Lovelace",
              "email": "not-an-email",
              "password": "weak",
              "confirmPassword": "weak"
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (errors, _) = await ReadFailureAsync(response);

        errors.Should().Contain("Email must be a valid email address");
        errors.Should().Contain("Password must be at least 8 characters long");

        // The assertion that EARNS the claim above. A first draft repeated the email assertion in
        // predicate form, which `Contain(string)` already means — it could not fail once the line
        // above passed. What actually makes a field matcher able to claim one rule without
        // swallowing the other is that no entry is a joined blob.
        errors.Should().OnlyContain(e => !e.Contains("; ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASingleBrokenRule_StillServesExactlyOneEntry_AndNoSemicolonJoin()
    {
        // The boundary case: with one failure there is nothing to join, so `message` and the single
        // `errors[0]` agree. Worth pinning because it is the shape most refusals actually have, and
        // a change that always wrapped or always joined would still pass the multi-rule tests.
        var response = await Client.PostAsync("/api/user/register/customer", Json("""
            {
              "firstName": "Ada",
              "lastName": "Lovelace",
              "email": "ada.single.rule@example.com",
              "password": "Str0ng!Passw0rd",
              "confirmPassword": "Different1!"
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (errors, message) = await ReadFailureAsync(response);

        errors.Should().ContainSingle().Which.Should().Be("Passwords do not match");
        message.Should().Be("Passwords do not match");
        message.Should().NotContain("; ");
    }
}
