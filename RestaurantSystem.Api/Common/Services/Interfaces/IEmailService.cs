using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Interface for email service operations
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
    Task SendPasswordResetEmailAsync(ApplicationUser user, string resetToken, string? resetUrl = null);

    /// <summary>
    /// Sends a welcome email to newly registered users
    /// </summary>
    /// <param name="user">The newly registered user</param>
    /// <returns>Task representing the async operation</returns>
    Task SendWelcomeEmailAsync(ApplicationUser user);

    /// <summary>
    /// Sends an email verification email
    /// </summary>
    /// <param name="user">The user to verify</param>
    /// <param name="verificationToken">Email verification token</param>
    /// <param name="verificationUrl">The complete verification URL (optional)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailVerificationAsync(ApplicationUser user, string verificationToken, string? verificationUrl = null);

    /// <summary>
    /// Sends a password changed notification email
    /// </summary>
    /// <param name="user">The user whose password was changed</param>
    /// <returns>Task representing the async operation</returns>
    Task SendPasswordChangedNotificationAsync(ApplicationUser user);

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
    Task SendReservationConfirmationEmailAsync(string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string? specialRequests = null);

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
    Task SendReservationApprovedEmailAsync(string customerEmail, string customerName, string tableNumber,
        DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests,
        string? specialRequests = null, string? notes = null);

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
    Task SendOrderReceivedEmailAsync(string customerEmail, string customerName, string orderNumber,
        string orderType, decimal total, IEnumerable<(string name, int quantity, decimal price)> items,
        string? specialInstructions = null, string? deliveryAddress = null);

    /// <summary>
    /// Sends order confirmed email to customer (when admin confirms the order)
    /// </summary>
    /// <param name="customerEmail">Customer email address</param>
    /// <param name="customerName">Customer name</param>
    /// <param name="orderNumber">Order number</param>
    /// <param name="orderType">Order type</param>
    /// <param name="estimatedPreparationMinutes">Estimated preparation time in minutes</param>
    /// <returns>Task representing the async operation</returns>
    Task SendOrderConfirmedEmailAsync(string customerEmail, string customerName, string orderNumber,
        string orderType, int estimatedPreparationMinutes);

    /// <summary>
    /// Send order cancellation email to customer
    /// </summary>
    Task SendOrderCancellationEmailAsync(string customerEmail, string customerName, string orderNumber,
        string cancellationReason);

    /// <summary>
    /// Send order delayed email to customer with approval options
    /// </summary>
    Task SendOrderDelayedEmailAsync(string customerEmail, string customerName, string orderNumber,
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
    /// <param name="specialInstructions">Special instructions</param>
    /// <param name="deliveryAddress">Delivery address (if applicable)</param>
    /// <returns>Task representing the async operation</returns>
    Task SendOrderConfirmationAdminEmailAsync(string adminEmail, string orderNumber, string customerName,
        string customerEmail, string customerPhone, string orderType, decimal total,
        IEnumerable<(string name, int quantity, decimal price)> items, string? specialInstructions = null,
        string? deliveryAddress = null);

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
    Task SendAccountDeletionEmailAsync(string toEmail, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate);
}
