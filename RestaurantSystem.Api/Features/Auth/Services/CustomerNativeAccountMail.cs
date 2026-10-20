using System.Globalization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>Reuses account templates/transport without exposing grants through log-only delivery.</summary>
public sealed class CustomerNativeAccountMail(ICustomerNativeAccountLinks links, IEmailBrandingProvider branding,
    IEmailSender sender, IOptions<EmailSettings> settings, ILogger<CustomerNativeAccountMail> logger)
    : ICustomerNativeAccountMail
{
    private const int MaximumRetries = 3;
    private const int MaximumRetryDelayMs = 10_000;

    public async Task SendAsync(CultureInfo culture, ApplicationUser user, string token, string action)
    {
        if (!settings.Value.EmailsEnabled || settings.Value.LogEmailsOnly)
        {
            logger.LogInformation("Native account email delivery is disabled; sensitive contents are withheld");
            return;
        }
        var link = links.Create(user, token, action);
        var brand = await branding.GetAsync();
        var verify = action == CustomerNativeAccountLinks.Verify;
        var subject = verify ? EmailTemplates.EmailVerification.GetSubject(culture, brand)
            : EmailTemplates.PasswordReset.GetSubject(culture, brand);
        var html = verify ? EmailTemplates.EmailVerification.GetHtmlBody(culture, brand, user.FirstName, user.LastName, link)
            : EmailTemplates.PasswordReset.GetHtmlBody(culture, brand, user.FirstName, user.LastName, link);
        var text = verify ? EmailTemplates.EmailVerification.GetTextBody(culture, brand, user.FirstName, user.LastName, link)
            : EmailTemplates.PasswordReset.GetTextBody(culture, brand, user.FirstName, user.LastName, link);
        var message = new OutgoingEmail(user.Email!, subject, html, text);
        var maximumRetries = Math.Clamp(settings.Value.MaxRetryAttempts, 0, MaximumRetries);
        for (var attempt = 0; attempt <= maximumRetries; attempt++)
        {
            string exceptionType;
            try
            {
                await sender.SendAsync(message, CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                exceptionType = ex.GetType().Name;
            }
            // Neither raw provider exceptions nor rendered email/recipient/grant enter logs.
            logger.LogWarning("Native account mail attempt failed with {ExceptionType}", exceptionType);
            if (attempt == maximumRetries) return;
            await Task.Delay(Math.Clamp(settings.Value.RetryDelayMs, 0, MaximumRetryDelayMs));
        }
    }
}
