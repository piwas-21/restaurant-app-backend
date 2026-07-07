namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation rejection email template (sent to customer)
    /// </summary>
    public static class ReservationRejected
    {
        public static string GetSubject(EmailBranding brand) => $"Reservation Update - {brand.Name}";

        public static string GetHtmlBody(EmailBranding brand, string customerName, DateTime reservationDate, TimeSpan startTime, int numberOfGuests, string contactEmail)
        {
            var email = contactEmail;
            var formattedDate = reservationDate.ToString("dddd, MMMM dd, yyyy");

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Reservation Update</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #d4af37; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #d4af37; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .notice {{ background: #fee; border: 1px solid #fcc; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>Reservation Update</h2>
            <p>Dear {customerName},</p>
            <p>We regret to inform you that we are unable to accommodate your reservation request at this time.</p>

            <div class='info-box'>
                <strong>📅 Date:</strong> {formattedDate}<br>
                <strong>🕐 Time:</strong> {startTime:hh\:mm}<br>
                <strong>👥 Guests:</strong> {numberOfGuests}
            </div>

            <div class='notice'>
                <strong>❌ We apologize for the inconvenience</strong><br>
                Unfortunately, we cannot confirm your reservation. This may be due to availability constraints or other factors.
            </div>

            <p>We encourage you to try booking for another date or time. You can make a new reservation on our website or contact us directly.</p>
            <p>If you have any questions, please don't hesitate to reach out to us at {email} or call us.</p>
            <p>We hope to welcome you soon!</p>
            <p>Best regards,<br>{brand.Name} Team</p>
        </div>
        <div class='footer'>
            <p>{brand.Name} | {brand.City} | {email}</p>
            <p>© {DateTime.UtcNow.Year} {brand.Name}. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(EmailBranding brand, string customerName, DateTime reservationDate, TimeSpan startTime, int numberOfGuests, string contactEmail)
        {
            var email = contactEmail;
            var formattedDate = reservationDate.ToString("dddd, MMMM dd, yyyy");

            return $@"{brand.Name} - Reservation Update

Dear {customerName},

We regret to inform you that we are unable to accommodate your reservation request at this time.

Requested Reservation:
Date: {formattedDate}
Time: {startTime:hh\:mm}
Guests: {numberOfGuests}

UNABLE TO CONFIRM
Unfortunately, we cannot confirm your reservation. This may be due to availability constraints or other factors.

We encourage you to try booking for another date or time. You can make a new reservation on our website or contact us directly.

If you have any questions, please don't hesitate to reach out to us at {email} or call us.

We hope to welcome you soon!

Best regards,
{brand.Name} Team

{brand.Name} | {brand.City} | {email}
© {DateTime.UtcNow.Year} {brand.Name}. All rights reserved.";
        }
    }
}
