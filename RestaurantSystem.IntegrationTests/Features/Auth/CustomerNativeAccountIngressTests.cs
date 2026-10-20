using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountIngressTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    [Theory]
    [InlineData("request-verification", "send-email-verification")]
    [InlineData("request-password-reset", "forgot-password")]
    public async Task Native_and_web_aliases_share_the_same_IP_budget(string native, string web)
    {
        AuthenticateAsAnonymous();
        for (var index = 0; index < 3; index++)
        {
            var response = index % 2 == 0
                ? await PostAsync(native, new { schemaVersion = 1, email = "unknown@example.test" })
                : await Client.PostAsJsonAsync("/api/Auth/" + web, new { email = "unknown@example.test" });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await Client.PostAsJsonAsync("/api/Auth/" + web, new { email = "unknown@example.test" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PostAsync(native, new { schemaVersion = 1, email = "unknown@example.test" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_per_address_verification_cooldown_is_shared_in_both_directions(bool nativeFirst)
    {
        var user = await CustomerAsync();
        AuthenticateAsAnonymous();
        if (nativeFirst)
        {
            await SendLinkAsync(user);
            (await Client.PostAsJsonAsync("/api/Auth/send-email-verification", new { email = user.Email })).StatusCode.Should().Be(HttpStatusCode.OK);
            Messages.Should().ContainSingle();
            LegacyMail.Invocations.Should().BeEmpty();
        }
        else
        {
            (await Client.PostAsJsonAsync("/api/Auth/send-email-verification", new { email = user.Email })).StatusCode.Should().Be(HttpStatusCode.OK);
            await SuccessAsync(await PostAsync("request-verification", new { schemaVersion = 1, email = user.Email }));
            Messages.Should().BeEmpty();
            LegacyMail.Verify(mail => mail.SendEmailVerificationAsync(It.IsAny<CultureInfo>(), It.IsAny<ApplicationUser>(), It.IsAny<string>(), null), Times.Once);
        }
    }

    [Theory]
    [InlineData("request-verification")]
    [InlineData("request-password-reset")]
    public async Task Staff_and_unknown_addresses_are_indistinguishable_and_neither_sends_native_mail(string action)
    {
        var staff = await CustomerAsync(UserRole.Admin);
        AuthenticateAsAnonymous();
        var known = await PostAsync(action, new { schemaVersion = 1, email = staff.Email });
        var unknown = await PostAsync(action, new { schemaVersion = 1, email = "unknown@example.test" });
        await SuccessAsync(known);
        (await known.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"email\":\"unknown@example.test\"}")]
    [InlineData("{\"schemaVersion\":2,\"email\":\"unknown@example.test\"}")]
    [InlineData("{\"schemaVersion\":1,\"email\":\"unknown@example.test\",\"redirectUrl\":\"https://other.example\"}")]
    public async Task Unknown_fields_and_missing_or_unsupported_schema_are_refused(string body)
    {
        AuthenticateAsAnonymous();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        (await Client.PostAsync(Route + "request-verification", content)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Legacy_requests_cannot_choose_native_delivery_through_an_ignored_internal_property()
    {
        var user = await CustomerAsync();
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync("/api/Auth/send-email-verification", new { email = user.Email, nativeClient = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Messages.Should().BeEmpty();
        LegacyMail.Verify(mail => mail.SendEmailVerificationAsync(It.IsAny<CultureInfo>(), It.IsAny<ApplicationUser>(), It.IsAny<string>(), null), Times.Once);
    }
}
