using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Email service: builds templated messages and delegates delivery to the configured
/// <see cref="IEmailSender"/> (SMTP or Resend). Owns enable/log-only checks and retries.
/// </summary>
public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly LocalizationSettings _localizationSettings;
    private readonly IEmailSender _emailSender;
    private readonly IEmailBrandingProvider _brandingProvider;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailSettings> emailSettings,
        IOptions<LocalizationSettings> localizationSettings,
        IEmailSender emailSender,
        IEmailBrandingProvider brandingProvider,
        ILogger<EmailService> logger)
    {
        _emailSettings = emailSettings.Value;
        _localizationSettings = localizationSettings.Value;
        _emailSender = emailSender;
        _brandingProvider = brandingProvider;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(ApplicationUser user, string resetToken, string? resetUrl = null)
    {
        try
        {
            // Generate reset URL if not provided
            if (string.IsNullOrEmpty(resetUrl))
            {
                resetUrl = $"{_emailSettings.FrontendBaseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(user.Email!)}";
            }

            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.PasswordReset.GetSubject(brand);
            var htmlBody = EmailTemplates.PasswordReset.GetHtmlBody(brand, user.FirstName, user.LastName, resetUrl);
            var textBody = EmailTemplates.PasswordReset.GetTextBody(brand, user.FirstName, user.LastName, resetUrl);

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Password reset email sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to user {UserId} ({Email})", user.Id, user.Email);
            throw;
        }
    }

    public async Task SendWelcomeEmailAsync(ApplicationUser user)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.Welcome.GetSubject(brand);
            var htmlBody = EmailTemplates.Welcome.GetHtmlBody(brand, user.FirstName, user.LastName, user.Role.ToString());
            var textBody = EmailTemplates.Welcome.GetTextBody(brand, user.FirstName, user.LastName, user.Role.ToString());

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Welcome email sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to user {UserId} ({Email})", user.Id, user.Email);
            // Non-fatal: welcome email failure should not block registration
        }
    }

    public async Task SendEmailVerificationAsync(ApplicationUser user, string verificationToken, string? verificationUrl = null)
    {
        try
        {
            // Generate verification URL if not provided
            if (string.IsNullOrEmpty(verificationUrl))
            {
                verificationUrl = $"{_emailSettings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(verificationToken)}&email={Uri.EscapeDataString(user.Email!)}";
            }

            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.EmailVerification.GetSubject(brand);
            var htmlBody = EmailTemplates.EmailVerification.GetHtmlBody(brand, user.FirstName, user.LastName, verificationUrl);
            var textBody = EmailTemplates.EmailVerification.GetTextBody(brand, user.FirstName, user.LastName, verificationUrl);

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Email verification sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email verification to user {UserId} ({Email})", user.Id, user.Email);
            // Non-fatal: email failure should not block registration
        }
    }

    public async Task SendPasswordChangedNotificationAsync(ApplicationUser user)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.PasswordChanged.GetSubject(brand);
            var htmlBody = EmailTemplates.PasswordChanged.GetHtmlBody(brand, user.FirstName, user.LastName, DateTime.UtcNow);
            var textBody = EmailTemplates.PasswordChanged.GetTextBody(brand, user.FirstName, user.LastName, DateTime.UtcNow);

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Password changed notification sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password changed notification to user {UserId} ({Email})", user.Id, user.Email);
            throw;
        }
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        if (!_emailSettings.EmailsEnabled)
        {
            _logger.LogInformation("Email sending is disabled. Would have sent email to {To} with subject: {Subject}", to, subject);
            return;
        }

        if (_emailSettings.LogEmailsOnly)
        {
            _logger.LogInformation("EMAIL LOG - To: {To}, Subject: {Subject}, Body: {Body}", to, subject, htmlBody);
            return;
        }

        var retryCount = 0;
        var maxRetries = _emailSettings.MaxRetryAttempts;

        while (retryCount <= maxRetries)
        {
            try
            {
                await _emailSender.SendAsync(new OutgoingEmail(to, subject, htmlBody, textBody), CancellationToken.None);

                _logger.LogInformation("Email sent successfully to {To} with subject: {Subject}", to, subject);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount > maxRetries)
                {
                    _logger.LogError(ex, "Failed to send email to {To} after {RetryCount} attempts", to, retryCount);
                    throw;
                }

                _logger.LogWarning(ex, "Failed to send email to {To}, attempt {RetryCount}/{MaxRetries}. Retrying...",
                    to, retryCount, maxRetries);

                await Task.Delay(_emailSettings.RetryDelayMs);
            }
        }
    }

    public async Task SendBulkEmailAsync(IEnumerable<string> recipients, string subject, string htmlBody, string? textBody = null)
    {
        var tasks = recipients.Select(recipient => SendEmailAsync(recipient, subject, htmlBody, textBody));

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogInformation("Bulk email sent to {RecipientCount} recipients with subject: {Subject}",
                recipients.Count(), subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bulk email to some recipients with subject: {Subject}", subject);
            throw;
        }
    }

    public async Task SendReservationConfirmationEmailAsync(string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string? specialRequests = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.ReservationConfirmation.GetSubject(brand);
            var htmlBody = EmailTemplates.ReservationConfirmation.GetHtmlBody(
                brand, customerName, tableNumber, reservationDate, startTime, endTime, numberOfGuests, _emailSettings.AdminEmail, specialRequests);
            var textBody = EmailTemplates.ReservationConfirmation.GetTextBody(
                brand, customerName, tableNumber, reservationDate, startTime, endTime, numberOfGuests, _emailSettings.AdminEmail, specialRequests);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Reservation confirmation email sent to {Email} for table {TableNumber} on {Date}",
                customerEmail, tableNumber, reservationDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reservation confirmation email to {Email}", customerEmail);
            throw;
        }
    }

    public async Task SendReservationApprovedEmailAsync(string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests,
        string? specialRequests = null, string? notes = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.ReservationApproved.GetSubject(brand);
            var htmlBody = EmailTemplates.ReservationApproved.GetHtmlBody(
                brand, customerName, tableNumber, reservationDate, startTime, endTime, numberOfGuests, _emailSettings.AdminEmail, specialRequests, notes);
            var textBody = EmailTemplates.ReservationApproved.GetTextBody(
                brand, customerName, tableNumber, reservationDate, startTime, endTime, numberOfGuests, _emailSettings.AdminEmail, specialRequests, notes);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Reservation approved email sent to {Email} for table {TableNumber} on {Date}",
                customerEmail, tableNumber, reservationDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reservation approved email to {Email}", customerEmail);
            throw;
        }
    }

    public async Task SendOrderReceivedEmailAsync(string customerEmail, string customerName, string orderNumber,
        string orderType, decimal total, IEnumerable<(string name, int quantity, decimal price)> items,
        string? specialInstructions = null, string? deliveryAddress = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderReceived.GetSubject(brand);
            var htmlBody = EmailTemplates.OrderReceived.GetHtmlBody(
                brand, customerName, orderNumber, orderType, total, _localizationSettings.Currency, items, _emailSettings.AdminEmail, specialInstructions, deliveryAddress);
            var textBody = EmailTemplates.OrderReceived.GetTextBody(
                brand, customerName, orderNumber, orderType, total, _localizationSettings.Currency, items, _emailSettings.AdminEmail, specialInstructions, deliveryAddress);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Order received email sent to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order received email to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
            throw;
        }
    }

    public async Task SendOrderConfirmedEmailAsync(string customerEmail, string customerName, string orderNumber,
        string orderType, int estimatedPreparationMinutes)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderConfirmed.GetSubject(brand);
            var htmlBody = EmailTemplates.OrderConfirmed.GetHtmlBody(
                brand, customerName, orderNumber, orderType, estimatedPreparationMinutes, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderConfirmed.GetTextBody(
                brand, customerName, orderNumber, orderType, estimatedPreparationMinutes, _emailSettings.AdminEmail);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Order confirmed email sent to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order confirmed email to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
            throw;
        }
    }

    public async Task SendOrderCancellationEmailAsync(string customerEmail, string customerName, string orderNumber,
        string cancellationReason)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderCancelled.GetSubject(brand);
            var htmlBody = EmailTemplates.OrderCancelled.GetHtmlBody(
                brand, customerName, orderNumber, cancellationReason, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderCancelled.GetTextBody(
                brand, customerName, orderNumber, cancellationReason, _emailSettings.AdminEmail);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Order cancellation email sent to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order cancellation email to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
            throw;
        }
    }

    public async Task SendOrderDelayedEmailAsync(string customerEmail, string customerName, string orderNumber,
        int delayMinutes, string approveUrl, string rejectUrl)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderDelayed.GetSubject(brand);
            var htmlBody = EmailTemplates.OrderDelayed.GetHtmlBody(
                brand, customerName, orderNumber, delayMinutes, approveUrl, rejectUrl, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderDelayed.GetTextBody(
                brand, customerName, orderNumber, delayMinutes, approveUrl, rejectUrl, _emailSettings.AdminEmail);

            await SendEmailAsync(customerEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Order delayed email sent to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order delayed email to {Email} for order {OrderNumber}",
                customerEmail, orderNumber);
            throw;
        }
    }

    public async Task SendOrderConfirmationAdminEmailAsync(string adminEmail, string orderNumber, string customerName,
        string customerEmail, string customerPhone, string orderType, decimal total,
        IEnumerable<(string name, int quantity, decimal price)> items, string? specialInstructions = null,
        string? deliveryAddress = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderConfirmationAdmin.GetSubject(brand);
            var baseUrl = _emailSettings.BackendBaseUrl;
            var frontendUrl = _emailSettings.FrontendBaseUrl;
            var htmlBody = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
                brand, orderNumber, customerName, customerEmail, customerPhone, orderType, total, _localizationSettings.Currency, items,
                baseUrl, frontendUrl, _emailSettings.AdminEmail,
                specialInstructions, deliveryAddress);
            var textBody = EmailTemplates.OrderConfirmationAdmin.GetTextBody(
                brand, orderNumber, customerName, customerEmail, customerPhone, orderType, total, _localizationSettings.Currency, items,
                _emailSettings.AdminEmail, specialInstructions, deliveryAddress);

            await SendEmailAsync(adminEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Order notification email sent to admin {Email} for order {OrderNumber}",
                adminEmail, orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order notification email to admin {Email} for order {OrderNumber}",
                adminEmail, orderNumber);
            throw;
        }
    }

    public async Task SendMembershipConfirmationEmailAsync(
        string toEmail,
        string userName,
        string groupName,
        string groupDescription,
        byte[] qrCodeImage,
        string qrCodeData,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync(cancellationToken);
            var subject = $"Welcome to {groupName}!";

            var expiryText = expiryDate.HasValue
                ? $"<p><strong>Membership Expires:</strong> {expiryDate.Value:MMMM dd, yyyy}</p>"
                : "";

            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4a90e2; color: white; padding: 20px; text-align: center; border-radius: 8px 8px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 30px; border-radius: 0 0 8px 8px; }}
        .qr-container {{ text-align: center; margin: 30px 0; padding: 20px; background: white; border-radius: 8px; }}
        .qr-code {{ max-width: 300px; height: auto; }}
        .button {{ display: inline-block; padding: 12px 24px; margin: 10px 5px; background-color: #4a90e2; color: white; text-decoration: none; border-radius: 6px; }}
        .footer {{ text-align: center; margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Welcome to {groupName}!</h1>
        </div>
        <div class=""content"">
            <p>Hello {userName},</p>
            <p>You have been successfully added to <strong>{groupName}</strong>.</p>
            <p>{groupDescription}</p>
            {expiryText}

            <div class=""qr-container"">
                <h3>Your Membership QR Code</h3>
                <img src=""cid:qrcode"" alt=""Membership QR Code"" class=""qr-code"" />
                <p style=""font-size: 12px; color: #666; margin-top: 10px;"">Show this QR code at the restaurant to receive your member benefits.</p>
            </div>

            <div style=""text-align: center; margin-top: 30px;"">
                <p><strong>Add to your wallet for easy access:</strong></p>
                <a href=""#"" class=""button"" style=""background-color: #000;"">📱 Add to Apple Wallet</a>
                <a href=""#"" class=""button"" style=""background-color: #4285f4;"">📱 Add to Google Wallet</a>
                <p style=""font-size: 12px; color: #999; margin-top: 10px;"">Wallet pass functionality coming soon!</p>
            </div>
        </div>
        <div class=""footer"">
            <p>{brand.Name} | Thank you for being a valued member!</p>
        </div>
    </div>
</body>
</html>";

            var textBody = $@"Welcome to {groupName}!

Hello {userName},

You have been successfully added to {groupName}.

{groupDescription}

{(expiryDate.HasValue ? $"Membership Expires: {expiryDate.Value:MMMM dd, yyyy}" : "")}

Your membership QR code is attached to this email. Show this QR code at the restaurant to receive your member benefits.

QR Code Data: {qrCodeData}

---
{brand.Name}
Thank you for being a valued member!";

            await SendEmailWithEmbeddedImageAsync(toEmail, subject, htmlBody, textBody, qrCodeImage, "qrcode", cancellationToken);

            _logger.LogInformation("Membership confirmation email sent to {Email} for group {GroupName}", toEmail, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send membership confirmation email to {Email} for group {GroupName}", toEmail, groupName);
            throw;
        }
    }

    public async Task SendAccountDeletionEmailAsync(string toEmail, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.AccountDeletion.GetSubject(brand);
            var htmlBody = EmailTemplates.AccountDeletion.GetHtmlBody(brand, firstName, lastName, deleteUrl, cancelUrl, scheduledDeletionDate);
            var textBody = EmailTemplates.AccountDeletion.GetTextBody(brand, firstName, lastName, deleteUrl, cancelUrl, scheduledDeletionDate);

            await SendEmailAsync(toEmail, subject, htmlBody, textBody);

            _logger.LogInformation("Account deletion email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send account deletion email to {Email}", toEmail);
            // Non-fatal: deletion is already scheduled in the DB
        }
    }

    private async Task SendEmailWithEmbeddedImageAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody,
        byte[] imageData,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (!_emailSettings.EmailsEnabled)
        {
            _logger.LogInformation("Email sending is disabled. Would have sent email to {To} with subject: {Subject}", to, subject);
            return;
        }

        if (_emailSettings.LogEmailsOnly)
        {
            _logger.LogInformation("EMAIL LOG - To: {To}, Subject: {Subject}, Has Embedded Image: true", to, subject);
            return;
        }

        var retryCount = 0;
        var maxRetries = _emailSettings.MaxRetryAttempts;

        while (retryCount <= maxRetries)
        {
            try
            {
                // The HTML references the image via cid:{contentId}; pass it as an inline
                // attachment so the sender (SMTP or Resend) embeds it accordingly.
                var inlineImage = new EmailAttachment($"{contentId}.png", imageData, "image/png", contentId);
                await _emailSender.SendAsync(
                    new OutgoingEmail(to, subject, htmlBody, textBody, [inlineImage]), cancellationToken);

                _logger.LogInformation("Email with embedded image sent successfully to {To}", to);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount > maxRetries)
                {
                    _logger.LogError(ex, "Failed to send email to {To} after {RetryCount} attempts", to, retryCount);
                    throw;
                }

                _logger.LogWarning(ex, "Failed to send email to {To}, attempt {RetryCount}/{MaxRetries}. Retrying...",
                    to, retryCount, maxRetries);

                await Task.Delay(_emailSettings.RetryDelayMs, cancellationToken);
            }
        }
    }

}
