using System.Globalization;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Interface for email service operations.
/// <para>
/// Every templated send takes the recipient's <see cref="CultureInfo"/> as its first argument.
/// It is explicit, never ambient: most of these are queued from a detached task, a webhook or a
/// BackgroundService where <see cref="CultureInfo.CurrentUICulture"/> is the server's, not the
/// guest's (EMAIL-LOCALISATION-PLAN §6.1). Since S5 every production caller passes what
/// <see cref="IEmailLanguageResolver"/> resolved for that recipient — the language frozen on the
/// order or reservation, the account's own preference, or the tenant's for the operator alerts —
/// resolved BEFORE any task is queued. Only the dev-only <c>EmailTestController</c> still names
/// <see cref="RestaurantSystem.Api.Common.Templates.EmailCultures.English"/> directly.
/// </para>
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a password reset email to the user
    /// </summary>
    /// <param name="user">The user requesting password reset</param>
    /// <param name="resetToken">The password reset token</param>
    /// <param name="resetUrl">The complete reset URL (optional, will be generated if not provided)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendPasswordResetEmailAsync(CultureInfo culture, ApplicationUser user, string resetToken, string? resetUrl = null);

    /// <summary>
    /// Sends a welcome email to newly registered users
    /// </summary>
    /// <param name="user">The newly registered user</param>
    /// <returns>Task representing the async operation</returns>
    Task SendWelcomeEmailAsync(CultureInfo culture, ApplicationUser user);

    /// <summary>
    /// Sends an email verification email
    /// </summary>
    /// <param name="user">The user to verify</param>
    /// <param name="verificationToken">Email verification token</param>
    /// <param name="verificationUrl">The complete verification URL (optional)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailVerificationAsync(CultureInfo culture, ApplicationUser user, string verificationToken, string? verificationUrl = null);

    /// <summary>
    /// Sends a password changed notification email
    /// </summary>
    /// <param name="user">The user whose password was changed</param>
    /// <returns>Task representing the async operation</returns>
    Task SendPasswordChangedNotificationAsync(CultureInfo culture, ApplicationUser user);

    /// <summary>
    /// Sends a generic email
    /// </summary>
    /// <param name="to">Recipient email address</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="textBody">Plain text body content (optional)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);

    /// <summary>
    /// Sends an email to multiple recipients
    /// </summary>
    /// <param name="recipients">List of recipient email addresses</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML body content</param>
    /// <param name="textBody">Plain text body content (optional)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendBulkEmailAsync(IEnumerable<string> recipients, string subject, string htmlBody, string? textBody = null);

    /// <summary>
    /// Sends reservation confirmation email (when reservation is created)
    /// </summary>
    /// <param name="customerEmail">Customer email address</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="tableNumber">Table number</param>
    /// <param name="reservationDate">Reservation date</param>
    /// <param name="startTime">Start time</param>
    /// <param name="endTime">End time</param>
    /// <param name="numberOfGuests">Number of guests</param>
    /// <param name="specialRequests">Special requests</param>
    /// <returns>Task representing the async operation</returns>
    Task SendReservationConfirmationEmailAsync(
        CultureInfo culture, string customerEmail, string customerName, ReservationMailDetails reservation);

    /// <summary>
    /// Sends reservation approved email (when admin approves the reservation)
    /// </summary>
    /// <param name="customerEmail">Customer email address</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="tableNumber">Table number</param>
    /// <param name="reservationDate">Reservation date</param>
    /// <param name="startTime">Start time</param>
    /// <param name="endTime">End time</param>
    /// <param name="numberOfGuests">Number of guests</param>
    /// <param name="specialRequests">Special requests</param>
    /// <param name="notes">Notes from restaurant</param>
    /// <returns>Task representing the async operation</returns>
    Task SendReservationApprovedEmailAsync(
        CultureInfo culture, string customerEmail, string customerName, ReservationMailDetails reservation,
        string? notes = null);

    /// <summary>
    /// Sends order received email to customer (when order is placed but not yet confirmed)
    /// </summary>
    /// <param name="customerEmail">Customer email address</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="orderNumber">Order number</param>
    /// <param name="orderType">Order type (DineIn, Takeaway, Delivery)</param>
    /// <param name="total">Order total amount</param>
    /// <param name="items">List of order items with name, quantity, and price</param>
    /// <param name="specialInstructions">Special instructions</param>
    /// <param name="deliveryAddress">Delivery address (if applicable)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendOrderReceivedEmailAsync(
        CultureInfo culture, string customerEmail, string customerName, OrderMailDetails order);

    /// <summary>
    /// Sends order confirmed email to customer (when admin confirms the order)
    /// </summary>
    /// <param name="customerEmail">Customer email address</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="orderNumber">Order number</param>
    /// <param name="orderType">Order type</param>
    /// <param name="estimatedPreparationMinutes">Estimated preparation time in minutes</param>
    /// <returns>Task representing the async operation</returns>
    Task SendOrderConfirmedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        string orderType, int estimatedPreparationMinutes);

    /// <summary>
    /// Send order cancellation email to customer
    /// </summary>
    Task SendOrderCancellationEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        string cancellationReason);

    /// <summary>
    /// Send order delayed email to customer with approval options
    /// </summary>
    Task SendOrderDelayedEmailAsync(CultureInfo culture, string customerEmail, string customerName, string orderNumber,
        int delayMinutes, string approveUrl, string rejectUrl);

    /// <summary>
    /// Sends order confirmation email to admin/restaurant
    /// </summary>
    /// <param name="adminEmail">Admin email address</param>
    /// <param name="orderNumber">Order number</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="customerEmail">Customer email</param>
    /// <param name="customerPhone">Customer phone</param>
    /// <param name="orderType">Order type (DineIn, Takeaway, Delivery)</param>
    /// <param name="total">Order total amount</param>
    /// <param name="items">List of order items with name, quantity, and price</param>
    /// <param name="quickActionToken">
    /// The order's <c>QuickActionToken</c>. The confirm/cancel buttons in this email are the only
    /// way to reach those anonymous endpoints, and the token is what authorises them
    /// (ORDER-TYPE-AVAILABILITY-PLAN §9.20) — omit it and the buttons land on "Order Not Found".
    /// </param>
    /// <param name="specialInstructions">Special instructions</param>
    /// <param name="deliveryAddress">Delivery address (if applicable)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendOrderConfirmationAdminEmailAsync(
        CultureInfo culture, string adminEmail, EmailGuest guest, OrderMailDetails order);

    /// <summary>
    /// Sends group membership confirmation email with QR code
    /// </summary>
    /// <param name="toEmail">Recipient email address</param>
    /// <param name="userName">User's name</param>
    /// <param name="groupName">Group name</param>
    /// <param name="groupDescription">Group description</param>
    /// <param name="qrCodeImage">QR code image as byte array</param>
    /// <param name="qrCodeData">QR code data string</param>
    /// <param name="expiryDate">Membership expiry date (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendMembershipConfirmationEmailAsync(
        CultureInfo culture,
        string toEmail,
        string userName,
        string groupName,
        string groupDescription,
        byte[] qrCodeImage,
        string qrCodeData,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends account deletion confirmation email
    /// </summary>
    Task SendAccountDeletionEmailAsync(CultureInfo culture, string toEmail, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate);
}
