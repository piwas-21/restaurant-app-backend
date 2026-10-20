using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public abstract class CustomerNativeAccountFixture(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected const string Password = "Str0ng!Passw0rd"; // pragma: allowlist secret (test-only)
    protected const string NewPassword = "An0ther!Passw0rd"; // pragma: allowlist secret (test-only)
    protected const string Route = "/api/customer-mobile/auth/";
    protected readonly List<OutgoingEmail> Messages = [];
    protected readonly Mock<IEmailService> LegacyMail = new();
    protected readonly Mock<IEmailSender> Sender = new();
    protected readonly NativeAccountClock Clock = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
            settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["accounts"];
        });
        services.PostConfigure<CustomerNativeAccountSettings>(settings =>
        {
            settings.Enabled = true;
            settings.LinkOrigin = "https://links.example";
            settings.TenantSlug = "alpha";
        });
        services.PostConfigure<EmailSettings>(settings =>
        {
            settings.EmailsEnabled = true;
            settings.LogEmailsOnly = false;
            settings.MaxRetryAttempts = 0;
        });
        services.RemoveAll<IEmailService>();
        services.AddSingleton(LegacyMail.Object);
        services.RemoveAll<IEmailSender>();
        Sender.Setup(sender => sender.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()))
            .Callback<OutgoingEmail, CancellationToken>((message, _) => Messages.Add(message))
            .Returns(Task.CompletedTask);
        services.AddSingleton(Sender.Object);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(Clock);
    }

    protected async Task<ApplicationUser> CustomerAsync(UserRole role = UserRole.Customer, bool confirmed = false)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            FirstName = "Native",
            LastName = "Customer",
            Role = role,
            EmailConfirmed = confirmed,
            CreatedBy = nameof(CustomerNativeAccountFixture),
            RefreshToken = string.Empty
        };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    protected async Task<ApplicationUser> ReadUserAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString()))!;
    }

    protected async Task ChangeUserAsync(Guid id, Action<ApplicationUser> change)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(id.ToString()))!;
        change(user);
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    protected Task<HttpResponseMessage> PostAsync(string path, object body) => Client.PostAsJsonAsync(Route + path, body);

    protected async Task<string> SendLinkAsync(ApplicationUser user, string action = "verify")
    {
        AuthenticateAsAnonymous();
        var path = action == "verify" ? "request-verification" : "request-password-reset";
        var response = await PostAsync(path, new { schemaVersion = 1, email = user.Email });
        await SuccessAsync(response);
        var message = Messages.Should().ContainSingle().Which;
        var matches = Regex.Matches(message.HtmlBody, @"https://links\.example/r/alpha/" + action + @"#proof=([A-Za-z0-9_-]+)");
        matches.Count.Should().BeGreaterThan(0);
        return matches[0].Groups[1].Value;
    }

    protected static async Task SuccessAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadFromJsonAsync<ApiResponse<string>>())!.Success.Should().BeTrue();
    }

    protected CustomerNativeAccountProof Decode(string proof, string action)
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICustomerNativeAccountLinks>().Read(proof, action);
    }

    protected sealed class NativeAccountClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan by) => _offset += by;
    }
}
