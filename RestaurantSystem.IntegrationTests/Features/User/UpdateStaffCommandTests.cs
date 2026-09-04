using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

/// <summary>
/// Issue #415 — two defects in <c>POST /api/User/update/staff</c>, both silent.
///
/// <para>
/// <b>1. An omitted <c>role</c> demoted the staff member.</b> <c>UserRole.Customer</c> is 0, so a
/// non-nullable enum bound an ABSENT key to Customer, `IsInEnum()` waved it through, and the
/// response said the update succeeded — while the administrator it edited lost every staff
/// permission. Nothing in the request mentioned the role.
/// </para>
///
/// <para>
/// <b>2. <c>UserName</c> was set to the FIRST NAME.</b> That is Identity's unique-username field,
/// so the first admin edit rewrote what <c>RegisterStaffCommand</c> had created as the email, and
/// two staff members called "Ali" could not both exist — the second <c>UpdateAsync</c> failed
/// Identity's DuplicateUserName check, refusing a routine edit of one person because of an
/// unrelated one.
/// </para>
///
/// <para>
/// Driven over HTTP with hand-built JSON rather than through the typed command, because the defect
/// IS the wire shape: a typed <c>UpdateStaffCommand</c> cannot express "the key is absent", which
/// is the input that broke. The C# object model is what hid this in the first place.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class UpdateStaffCommandTests : IntegrationTestBase
{
    private const string Endpoint = "/api/User/update/staff";

    public UpdateStaffCommandTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    /// <summary>
    /// Created through <see cref="UserManager{T}"/>, exactly as <c>RegisterStaffCommand</c> does —
    /// not by inserting a row. A hand-inserted `ApplicationUser` has no security stamp and no
    /// normalised keys, so it is not a user Identity can operate on, and every success path here
    /// answered 500 against one. A fixture the platform cannot produce proves nothing about a
    /// defect the platform has.
    /// </summary>
    private async Task<ApplicationUser> SeedStaffAsync(string firstName, UserRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"{firstName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            FirstName = firstName,
            LastName = "Staff",
            Role = role,
            PhoneNumber = "+41791234567",
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            RefreshToken = string.Empty,
        };

        var created = await users.CreateAsync(user, "Str0ng!Passw0rd#2026");
        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<ApplicationUser> ReloadAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id);
    }

    [Fact]
    public async Task An_omitted_role_is_REFUSED_rather_than_read_as_Customer()
    {
        AuthenticateAsAdmin();
        var staff = await SeedStaffAsync("Ada", UserRole.Admin);

        // No `role` key at all — the exact body the defect needed. Everything else is present, so a
        // refusal here can only be about the field under test.
        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            userId = staff.Id,
            firstName = "Ada",
            lastName = "Staff",
            email = staff.Email,
            phoneNumber = "+41791234567",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an update that never mentions the role must not choose one");
        (await ReloadAsync(staff.Id)).Role.Should().Be(UserRole.Admin,
            "the administrator kept every permission — this is the assertion the defect failed");
    }

    /// <summary>
    /// The control. A refusal of EVERY request would satisfy the test above while breaking the one
    /// screen that calls this endpoint, and the admin UI always sends the field.
    /// </summary>
    [Fact]
    public async Task An_explicit_role_still_changes_it()
    {
        AuthenticateAsAdmin();
        var staff = await SeedStaffAsync("Grace", UserRole.Server);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            userId = staff.Id,
            firstName = "Grace",
            lastName = "Staff",
            email = staff.Email,
            phoneNumber = "+41791234567",
            role = nameof(UserRole.Cashier),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await ReloadAsync(staff.Id)).Role.Should().Be(UserRole.Cashier);
    }

    /// <summary>
    /// The second discrimination control: "Customer" is still reachable when an admin actually asks
    /// for it. The fix refuses ABSENCE, not the value the absence used to be mistaken for — and
    /// conflating those would make the demotion impossible to perform deliberately.
    /// </summary>
    [Fact]
    public async Task An_EXPLICIT_Customer_role_is_still_accepted()
    {
        AuthenticateAsAdmin();
        var staff = await SeedStaffAsync("Alan", UserRole.Server);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            userId = staff.Id,
            firstName = "Alan",
            lastName = "Staff",
            email = staff.Email,
            phoneNumber = "+41791234567",
            role = nameof(UserRole.Customer),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReloadAsync(staff.Id)).Role.Should().Be(UserRole.Customer);
    }

    [Fact]
    public async Task The_username_stays_the_email_rather_than_becoming_the_first_name()
    {
        AuthenticateAsAdmin();
        var staff = await SeedStaffAsync("Ali", UserRole.Server);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            userId = staff.Id,
            firstName = "Ali",
            lastName = "Staff",
            email = staff.Email,
            phoneNumber = "+41791234567",
            role = nameof(UserRole.Server),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReloadAsync(staff.Id)).UserName.Should().Be(staff.Email,
            "UserName is Identity's UNIQUE field; RegisterStaffCommand creates it as the email");
    }

    /// <summary>
    /// The email-change branch, which every other test here skips by sending the address unchanged
    /// — and it is the branch the username assignment's PLACEMENT is about. `ChangeEmailAsync`
    /// saves rather than stages, so the username has to be re-derived from what actually landed.
    /// </summary>
    [Fact]
    public async Task A_changed_email_carries_the_username_with_it()
    {
        AuthenticateAsAdmin();
        var staff = await SeedStaffAsync("Edsger", UserRole.Server);
        var newEmail = $"edsger-{Guid.NewGuid():N}@example.com";

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            userId = staff.Id,
            firstName = "Edsger",
            lastName = "Staff",
            email = newEmail,
            phoneNumber = "+41791234567",
            role = nameof(UserRole.Server),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var reloaded = await ReloadAsync(staff.Id);
        reloaded.Email.Should().Be(newEmail);
        reloaded.UserName.Should().Be(newEmail, "the username tracks the address that actually landed");
    }

    /// <summary>
    /// The consequence, not just the field. Two staff members who share a first name must both be
    /// editable — with <c>UserName = FirstName</c> the second edit failed Identity's
    /// DuplicateUserName check, so one person's routine edit was refused because of another person
    /// entirely. Asserting the username alone would not have shown that.
    /// </summary>
    [Fact]
    public async Task Two_staff_members_sharing_a_first_name_can_both_be_edited()
    {
        AuthenticateAsAdmin();
        var first = await SeedStaffAsync("Ali", UserRole.Server);
        var second = await SeedStaffAsync("Ali", UserRole.Cashier);

        foreach (var staff in new[] { first, second })
        {
            var response = await Client.PostAsJsonAsync(Endpoint, new
            {
                userId = staff.Id,
                firstName = "Ali",
                lastName = "Edited",
                email = staff.Email,
                phoneNumber = "+41791234567",
                role = staff.Role.ToString(),
            });

            // The BODY, not the status. This endpoint answers 200 for a refusal too — an
            // `IdentityFailure` becomes `ApiResponse.Failure` and `UserController` returns
            // `Ok(result)` — so `Be(HttpStatusCode.OK)` is true for every outcome short of an
            // unhandled exception, and would have passed while the edit was being refused.
            var body = await ReadResponseAsync<ApiResponse<AuthResponse>>(response);
            body!.Success.Should().BeTrue(
                $"editing {staff.Email} must not be refused because of an unrelated namesake: "
                + string.Join("; ", body.Errors ?? []));
        }

        (await ReloadAsync(first.Id)).LastName.Should().Be("Edited");
        (await ReloadAsync(second.Id)).LastName.Should().Be("Edited");
    }
}
