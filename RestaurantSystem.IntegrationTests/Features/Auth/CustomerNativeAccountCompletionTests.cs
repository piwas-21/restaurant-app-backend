using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountCompletionTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    [Fact]
    public async Task Verification_is_explicit_and_does_not_issue_a_session_or_expose_email_or_identity_token()
    {
        var user = await CustomerAsync();
        var proof = await SendLinkAsync(user);
        (await ReadUserAsync(user.Id)).EmailConfirmed.Should().BeFalse("delivery/opening is not consent to completion");
        var decoded = Decode(proof, "verify");
        decoded.UserId.Should().Be(user.Id);
        decoded.Email.Should().Be(user.Email);
        var response = await PostAsync("verify", new { schemaVersion = 1, proof });
        await SuccessAsync(response);
        (await ReadUserAsync(user.Id)).EmailConfirmed.Should().BeTrue();
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(user.Email).And.NotContain(decoded.Token).And.NotContain(proof)
            .And.NotContain("accessToken").And.NotContain("refreshToken");
        await SuccessAsync(await PostAsync("verify", new { schemaVersion = 1, proof }));
    }

    [Fact]
    public async Task Password_reset_uses_the_original_identity_token_and_revokes_all_refresh_sessions()
    {
        var user = await CustomerAsync(confirmed: true);
        string refresh;
        using (var scope = Factory.Services.CreateScope())
            refresh = await scope.ServiceProvider.GetRequiredService<IRefreshSessionService>().IssueAsync(user, default);
        var proof = await SendLinkAsync(user, "recover");
        await SuccessAsync(await PostAsync("reset-password", new { schemaVersion = 1, proof, newPassword = NewPassword, confirmPassword = NewPassword }));
        using var check = Factory.Services.CreateScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var changed = (await users.FindByIdAsync(user.Id.ToString()))!;
        (await users.CheckPasswordAsync(changed, NewPassword)).Should().BeTrue();
        (await users.CheckPasswordAsync(changed, Password)).Should().BeFalse();
        (await check.ServiceProvider.GetRequiredService<IRefreshSessionService>().RotateAsync(changed, refresh, default)).Should().BeNull();
        var replay = await PostAsync("reset-password", new { schemaVersion = 1, proof, newPassword = Password, confirmPassword = Password });
        (await replay.Content.ReadFromJsonAsync<ApiResponse<string>>())!.Success.Should().BeFalse();
        (await users.CheckPasswordAsync(changed, NewPassword)).Should().BeTrue();
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("staff")]
    [InlineData("changed-email")]
    public async Task A_link_cannot_follow_an_erased_account_changed_role_or_reassigned_address(string change)
    {
        var user = await CustomerAsync();
        var proof = await SendLinkAsync(user);
        await ChangeUserAsync(user.Id, current =>
        {
            if (change == "deleted") current.IsDeleted = true;
            if (change == "staff") current.Role = UserRole.Admin;
            if (change == "changed-email") current.Email = "changed@example.test";
        });
        (await PostAsync("verify", new { schemaVersion = 1, proof })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadUserAsync(user.Id)).EmailConfirmed.Should().BeFalse();
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("tamper")]
    [InlineData("expiry")]
    public async Task Wrong_purpose_modified_and_expired_grants_cannot_complete_a_different_action(string defect)
    {
        var user = await CustomerAsync();
        var proof = await SendLinkAsync(user);
        if (defect == "expiry") Clock.Advance(TimeSpan.FromHours(3));
        if (defect == "tamper") proof = proof[..100] + (proof[100] == 'a' ? 'b' : 'a') + proof[101..];
        var response = defect == "purpose"
            ? await PostAsync("reset-password", new { schemaVersion = 1, proof, newPassword = NewPassword, confirmPassword = NewPassword })
            : await PostAsync("verify", new { schemaVersion = 1, proof });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadUserAsync(user.Id)).EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Registration_sends_a_native_link_and_creates_no_refresh_session()
    {
        AuthenticateAsAnonymous();
        const string email = "native-registration@example.test";
        var response = await PostAsync("register", new { schemaVersion = 1, firstName = "Native", lastName = "Customer", email, password = Password, confirmPassword = Password });
        await SuccessAsync(response);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("accessToken").And.NotContain("refreshToken");
        Messages.Should().ContainSingle().Which.HtmlBody.Should().Contain("/r/alpha/verify#proof=");
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.Role.Should().Be(UserRole.Customer);
        user.EmailConfirmed.Should().BeFalse();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        context.RefreshSessions.Where(row => row.UserId == user.Id).Should().BeEmpty();
        LegacyMail.Invocations.Should().BeEmpty();
    }
}
