namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation confirmation email template (sent to customer and admin)
    /// </summary>
    public static class ReservationConfirmation
    {
        public static string Subject => "Reservation Confirmation - Rumi Restaurant";

        public static string GetHtmlBody(string customerName, string tableNumber, DateTime reservationDate,
            TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string contactEmail,
            string? specialRequests = null)
        {
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"<div class='info-box'>
                        <strong>Special Requests:</strong><br>
                        {specialRequests}
                    </div>";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Reservation Confirmation</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #d4af37; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #d4af37; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .pending {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ Rumi Restaurant</h1>
        </div>
        <div class='content'>
            <h2>Reservation Received</h2>
            <p>Dear {customerName},</p>
            <p>Thank you for your reservation request at Rumi Restaurant. We have received your booking details:</p>

            <div class='info-box'>
                <strong>📅 Date:</strong> {reservationDate:dddd, MMMM dd, yyyy}<br>
                <strong>🕐 Time:</strong> {startTime:hh':'mm} - {endTime:hh':'mm}<br>
                <strong>👥 Guests:</strong> {numberOfGuests}<br>
                <strong>🪑 Table:</strong> {tableNumber}
            </div>

            {requestsSection}

            <div class='pending'>
                <strong>⏳ Pending Confirmation</strong><br>
                Your reservation is currently pending. Our team will review your request and send you a confirmation email shortly.
            </div>

            <p>If you need to make any changes or have questions, please contact us at {email}</p>
            <p>We look forward to serving you!</p>
            <p>Best regards,<br>Rumi Restaurant Team</p>
        </div>
        <div class='footer'>
            <p>Rumi Restaurant | Geneva | {email}</p>
            <p>© 2024 Rumi Restaurant. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(string customerName, string tableNumber, DateTime reservationDate,
            TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string contactEmail,
            string? specialRequests = null)
        {
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

Special Requests:
{specialRequests}";

            return $@"Rumi Restaurant - Reservation Received

Dear {customerName},

Thank you for your reservation request at Rumi Restaurant. We have received your booking details:

Date: {reservationDate:dddd, MMMM dd, yyyy}
Time: {startTime:hh':'mm} - {endTime:hh':'mm}
Guests: {numberOfGuests}
Table: {tableNumber}{requestsSection}

PENDING CONFIRMATION
Your reservation is currently pending. Our team will review your request and send you a confirmation email shortly.

If you need to make any changes or have questions, please contact us at {email}

We look forward to serving you!

Best regards,
Rumi Restaurant Team

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
