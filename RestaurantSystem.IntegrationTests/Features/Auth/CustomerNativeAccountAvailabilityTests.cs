using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountAvailabilityTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    [Theory]
    [InlineData("native-disabled")]
    [InlineData("mobile-disabled")]
    [InlineData("read-only")]
    [InlineData("accounts-disabled")]
    public async Task Every_native_action_requires_current_account_capability_and_separate_link_activation(string scenario)
    {
        AuthenticateAsAnonymous();
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        var native = scope.ServiceProvider.GetRequiredService<IOptions<CustomerNativeAccountSettings>>().Value;
        if (scenario == "native-disabled") native.Enabled = false;
        if (scenario == "mobile-disabled") mobile.Enabled = false;
        if (scenario == "read-only") mobile.Lifecycle = "read-only";
        if (scenario == "accounts-disabled") mobile.EnabledCapabilities = ["menu"];
        var bodies = new Dictionary<string, object>
        {
            ["register"] = new { schemaVersion = 1, firstName = "Native", lastName = "Customer", email = "absent@example.test", password = Password, confirmPassword = Password },
            ["request-verification"] = new { schemaVersion = 1, email = "absent@example.test" },
            ["request-password-reset"] = new { schemaVersion = 1, email = "absent@example.test" },
            ["verify"] = new { schemaVersion = 1, proof = new string('a', 100) },
            ["reset-password"] = new { schemaVersion = 1, proof = new string('a', 100), newPassword = NewPassword, confirmPassword = NewPassword }
        };
        foreach (var (path, body) in bodies)
            (await PostAsync(path, body)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        Messages.Should().BeEmpty();
        LegacyMail.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_grant_from_the_same_keyring_but_another_tenant_cannot_complete()
    {
        var user = await CustomerAsync();
        var proof = await SendLinkAsync(user);
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.TenantId = Guid.NewGuid();
        (await PostAsync("verify", new { schemaVersion = 1, proof })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadUserAsync(user.Id)).EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task An_identity_token_invalidated_by_a_password_change_cannot_be_replaced_or_replayed()
    {
        var user = await CustomerAsync(confirmed: true);
        var proof = await SendLinkAsync(user, "recover");
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RestaurantSystem.Domain.Entities.ApplicationUser>>();
            var current = (await users.FindByIdAsync(user.Id.ToString()))!;
            (await users.ChangePasswordAsync(current, Password, NewPassword)).Succeeded.Should().BeTrue();
        }
        var result = await PostAsync("reset-password", new { schemaVersion = 1, proof, newPassword = Password, confirmPassword = Password });
        (await result.Content.ReadFromJsonAsync<RestaurantSystem.Api.Common.Models.ApiResponse<string>>())!.Success.Should().BeFalse();
    }
}
