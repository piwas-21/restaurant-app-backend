using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public sealed class CustomerNativeAccountMailTests
{
    private const string PrivateUrl = "https://links.example/r/alpha/verify#proof=private-opaque-action-grant";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Disabled_or_log_only_delivery_neither_generates_nor_logs_a_grant(bool enabled, bool logOnly)
    {
        var links = new Mock<ICustomerNativeAccountLinks>(MockBehavior.Strict);
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        var logger = new Mock<ILogger<CustomerNativeAccountMail>>();
        var service = new CustomerNativeAccountMail(links.Object, Mock.Of<IEmailBrandingProvider>(), sender.Object,
            Options.Create(new EmailSettings { EmailsEnabled = enabled, LogEmailsOnly = logOnly }), logger.Object);
        await service.SendAsync(CultureInfo.GetCultureInfo("en"), User(), "private-identity-token", "verify");
        links.VerifyNoOtherCalls();
        sender.VerifyNoOtherCalls();
        AssertPrivateLogs(logger);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("recover")]
    public async Task Native_mail_reuses_account_templates_and_bounded_transport_retries_without_raw_exception_logging(string action)
    {
        var links = new Mock<ICustomerNativeAccountLinks>();
        links.Setup(value => value.Create(It.IsAny<ApplicationUser>(), "private-identity-token", action)).Returns(PrivateUrl);
        var branding = new Mock<IEmailBrandingProvider>();
        branding.Setup(value => value.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EmailBranding("Fixture", "City", "support@example.test"));
        var sender = new Mock<IEmailSender>();
        sender.Setup(value => value.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(PrivateUrl + " private-identity-token private@example.test"));
        var logger = new Mock<ILogger<CustomerNativeAccountMail>>();
        var service = new CustomerNativeAccountMail(links.Object, branding.Object, sender.Object,
            Options.Create(new EmailSettings { MaxRetryAttempts = 1, RetryDelayMs = 0 }), logger.Object);
        await service.SendAsync(CultureInfo.GetCultureInfo("fr"), User(), "private-identity-token", action);
        sender.Verify(value => value.SendAsync(It.Is<OutgoingEmail>(mail => mail.HtmlBody.Contains(PrivateUrl)
            && mail.TextBody != null && mail.TextBody.Contains(PrivateUrl)), It.IsAny<CancellationToken>()), Times.Exactly(2));
        logger.Invocations.Should().HaveCount(2);
        AssertPrivateLogs(logger);
    }

    private static ApplicationUser User() => new()
    {
        Email = "private@example.test",
        UserName = "private@example.test",
        FirstName = "Private",
        LastName = "User",
        Role = RestaurantSystem.Domain.Common.Enums.UserRole.Customer,
        CreatedBy = nameof(CustomerNativeAccountMailTests),
        RefreshToken = string.Empty
    };

    private static void AssertPrivateLogs(Mock<ILogger<CustomerNativeAccountMail>> logger)
    {
        foreach (var logged in logger.Invocations)
        {
            logged.Arguments[2].ToString().Should().NotContain("private-").And.NotContain("private@example.test");
            logged.Arguments[3].Should().BeNull("raw provider failures may contain a rendered email or grant");
        }
    }
}
