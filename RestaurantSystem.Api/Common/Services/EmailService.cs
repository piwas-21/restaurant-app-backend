using System.Globalization;
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
    private readonly ITenantClock _clock;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailSettings> emailSettings,
        IOptions<LocalizationSettings> localizationSettings,
        IEmailSender emailSender,
        IEmailBrandingProvider brandingProvider,
        ITenantClock clock,
        ILogger<EmailService> logger)
    {
        _emailSettings = emailSettings.Value;
        _localizationSettings = localizationSettings.Value;
        _emailSender = emailSender;
        _brandingProvider = brandingProvider;
        _clock = clock;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(CultureInfo culture, ApplicationUser user, string resetToken, string? resetUrl = null)
    {
        try
        {
            // Generate reset URL if not provided
            if (string.IsNullOrEmpty(resetUrl))
            {
                resetUrl = $"{_emailSettings.FrontendBaseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(user.Email!)}";
            }

            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.PasswordReset.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.PasswordReset.GetHtmlBody(culture, brand, user.FirstName, user.LastName, resetUrl);
            var textBody = EmailTemplates.PasswordReset.GetTextBody(culture, brand, user.FirstName, user.LastName, resetUrl);

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Password reset email sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to user {UserId} ({Email})", user.Id, user.Email);
            throw;
        }
    }

    public async Task SendWelcomeEmailAsync(CultureInfo culture, ApplicationUser user)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.Welcome.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.Welcome.GetHtmlBody(culture, brand, user.FirstName, user.LastName, user.Role.ToString());
            var textBody = EmailTemplates.Welcome.GetTextBody(culture, brand, user.FirstName, user.LastName, user.Role.ToString());

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Welcome email sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to user {UserId} ({Email})", user.Id, user.Email);
            // Non-fatal: welcome email failure should not block registration
        }
    }

    public async Task SendEmailVerificationAsync(CultureInfo culture, ApplicationUser user, string verificationToken, string? verificationUrl = null)
    {
        try
        {
            // Generate verification URL if not provided
            if (string.IsNullOrEmpty(verificationUrl))
            {
                verificationUrl = $"{_emailSettings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(verificationToken)}&email={Uri.EscapeDataString(user.Email!)}";
            }

            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.EmailVerification.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.EmailVerification.GetHtmlBody(culture, brand, user.FirstName, user.LastName, verificationUrl);
            var textBody = EmailTemplates.EmailVerification.GetTextBody(culture, brand, user.FirstName, user.LastName, verificationUrl);

            await SendEmailAsync(user.Email!, subject, htmlBody, textBody);

            _logger.LogInformation("Email verification sent to user {UserId} ({Email})", user.Id, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email verification to user {UserId} ({Email})", user.Id, user.Email);
            // Non-fatal: email failure should not block registration
        }
    }

    public async Task SendPasswordChangedNotificationAsync(CultureInfo culture, ApplicationUser user)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();

            // ONE reading of the clock for both bodies. Two calls to DateTime.UtcNow can straddle a
            // minute boundary, which is a mail whose HTML and plain-text halves disagree about when
            // the password changed — on the one mail whose entire job is to let a user say "that
            // was not me".
            var changedAt = _clock.Now;

            var subject = EmailTemplates.PasswordChanged.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.PasswordChanged.GetHtmlBody(culture, brand, user.FirstName, user.LastName, changedAt);
            var textBody = EmailTemplates.PasswordChanged.GetTextBody(culture, brand, user.FirstName, user.LastName, changedAt);

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

    public async Task SendReservationConfirmationEmailAsync(CultureInfo culture, string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string? specialRequests = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.ReservationConfirmation.GetSubject(culture, brand);
            var reservation = new ReservationMailDetails(
                reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests);
            var htmlBody = EmailTemplates.ReservationConfirmation.GetHtmlBody(
                culture, brand, customerName, reservation, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.ReservationConfirmation.GetTextBody(
                culture, brand, customerName, reservation, _emailSettings.AdminEmail);

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

    public async Task SendReservationApprovedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests,
        string? specialRequests = null, string? notes = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.ReservationApproved.GetSubject(culture, brand);
            var reservation = new ReservationMailDetails(
                reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests);
            var htmlBody = EmailTemplates.ReservationApproved.GetHtmlBody(
                culture, brand, customerName, reservation, _emailSettings.AdminEmail, notes);
            var textBody = EmailTemplates.ReservationApproved.GetTextBody(
                culture, brand, customerName, reservation, _emailSettings.AdminEmail, notes);

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

    public async Task SendOrderReceivedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        string orderType, decimal total, IEnumerable<(string name, int quantity, decimal price)> items,
        string? specialInstructions = null, string? deliveryAddress = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderReceived.GetSubject(culture, brand);
            var order = new OrderMailDetails(
                orderNumber, orderType, total, _localizationSettings.Currency, items,
                SpecialInstructions: specialInstructions, DeliveryAddress: deliveryAddress);
            var htmlBody = EmailTemplates.OrderReceived.GetHtmlBody(
                culture, brand, customerName, order, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderReceived.GetTextBody(
                culture, brand, customerName, order, _emailSettings.AdminEmail);

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

    public async Task SendOrderConfirmedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        string orderType, int estimatedPreparationMinutes)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderConfirmed.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.OrderConfirmed.GetHtmlBody(
                culture, brand, customerName, orderNumber, orderType, estimatedPreparationMinutes, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderConfirmed.GetTextBody(
                culture, brand, customerName, orderNumber, orderType, estimatedPreparationMinutes, _emailSettings.AdminEmail);

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

    public async Task SendOrderCancellationEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        string cancellationReason)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderCancelled.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.OrderCancelled.GetHtmlBody(
                culture, brand, customerName, orderNumber, cancellationReason, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderCancelled.GetTextBody(
                culture, brand, customerName, orderNumber, cancellationReason, _emailSettings.AdminEmail);

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

    public async Task SendOrderDelayedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        int delayMinutes, string approveUrl, string rejectUrl)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderDelayed.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.OrderDelayed.GetHtmlBody(
                culture, brand, customerName, orderNumber, delayMinutes, approveUrl, rejectUrl, _emailSettings.AdminEmail);
            var textBody = EmailTemplates.OrderDelayed.GetTextBody(
                culture, brand, customerName, orderNumber, delayMinutes, approveUrl, rejectUrl, _emailSettings.AdminEmail);

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

    public async Task SendOrderConfirmationAdminEmailAsync(CultureInfo culture, string adminEmail, string orderNumber, string customerName,
        string customerEmail, string customerPhone, string orderType, decimal total,
        IEnumerable<(string name, int quantity, decimal price)> items, string? quickActionToken,
        string? specialInstructions = null, string? deliveryAddress = null)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();
            var subject = EmailTemplates.OrderConfirmationAdmin.GetSubject(culture, brand);
            var baseUrl = _emailSettings.BackendBaseUrl;
            var frontendUrl = _emailSettings.FrontendBaseUrl;
            var guest = new EmailGuest(customerName, customerEmail, customerPhone);
            var order = new OrderMailDetails(
                orderNumber, orderType, total, _localizationSettings.Currency, items,
                quickActionToken, specialInstructions, deliveryAddress);
            var htmlBody = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
                culture, brand, guest, order,
                new EmailLinks(baseUrl, frontendUrl, _emailSettings.AdminEmail));
            var textBody = EmailTemplates.OrderConfirmationAdmin.GetTextBody(
                culture, brand, guest, order, _emailSettings.AdminEmail);

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
        CultureInfo culture,
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
            var subject = EmailTemplates.MembershipConfirmation.GetSubject(culture, groupName);
            var htmlBody = EmailTemplates.MembershipConfirmation.GetHtmlBody(
                culture, brand, userName, groupName, groupDescription, expiryDate);
            var textBody = EmailTemplates.MembershipConfirmation.GetTextBody(
                culture, brand, userName, groupName, groupDescription, qrCodeData, expiryDate);

            await SendEmailWithEmbeddedImageAsync(toEmail, subject, htmlBody, textBody, qrCodeImage, "qrcode", cancellationToken);

            _logger.LogInformation("Membership confirmation email sent to {Email} for group {GroupName}", toEmail, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send membership confirmation email to {Email} for group {GroupName}", toEmail, groupName);
            throw;
        }
    }

    public async Task SendAccountDeletionEmailAsync(CultureInfo culture, string toEmail, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
    {
        try
        {
            var brand = await _brandingProvider.GetAsync();

            // A DAY, on the restaurant's calendar. The stored value is `DateTime.UtcNow.AddDays(30)`,
            // so late-evening local requests fall on the NEXT UTC day and the mail named a date one
            // later than the account actually survives to. No offset marker: a bare date carries a
            // clock nobody promised, and the sweep is a background job with hours of slack anyway.
            var deletionDay = _clock.ToTenantTime(scheduledDeletionDate).Date;

            var subject = EmailTemplates.AccountDeletion.GetSubject(culture, brand);
            var htmlBody = EmailTemplates.AccountDeletion.GetHtmlBody(culture, brand, firstName, lastName, deleteUrl, cancelUrl, deletionDay);
            var textBody = EmailTemplates.AccountDeletion.GetTextBody(culture, brand, firstName, lastName, deleteUrl, cancelUrl, deletionDay);

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
