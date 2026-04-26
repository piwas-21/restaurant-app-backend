namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order cancellation email template (sent to customer)
    /// </summary>
    public static class OrderCancelled
    {
        public static string Subject => "Order Cancelled - Rumi Restaurant";

        public static string GetHtmlBody(string customerName, string orderNumber, string cancellationReason, string contactEmail)
        {
            var email = contactEmail;
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Order Cancelled</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #dc2626; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #dc2626; }}
        .order-number {{ background: #fee2e2; color: #991b1b; padding: 20px; border-radius: 5px; text-align: center; margin: 20px 0; }}
        .order-number-value {{ font-size: 28px; font-weight: bold; letter-spacing: 2px; }}
        .order-number-label {{ font-size: 14px; margin-top: 5px; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ Rumi Restaurant</h1>
        </div>
        <div class='content'>
            <h2>Order Cancelled</h2>
            <p>Dear {customerName},</p>
            <p>We regret to inform you that your order has been cancelled.</p>

            <div class='order-number'>
                <div class='order-number-label'>ORDER NUMBER</div>
                <div class='order-number-value'>{orderNumber}</div>
            </div>

            <div class='info-box'>
                <strong>Cancellation Reason:</strong><br>
                {cancellationReason}
            </div>

            <p>If you have any questions or concerns, please don't hesitate to contact us at {email} or call us.</p>
            <p>We apologize for any inconvenience and hope to serve you again soon.</p>
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

        public static string GetTextBody(string customerName, string orderNumber, string cancellationReason, string contactEmail)
        {
            var email = contactEmail;
            return $@"Rumi Restaurant - Order Cancelled

Dear {customerName},

We regret to inform you that your order has been cancelled.

Order Number: {orderNumber}

Cancellation Reason:
{cancellationReason}

If you have any questions or concerns, please don't hesitate to contact us at {email} or call us.

We apologize for any inconvenience and hope to serve you again soon.

Best regards,
Rumi Restaurant Team

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
