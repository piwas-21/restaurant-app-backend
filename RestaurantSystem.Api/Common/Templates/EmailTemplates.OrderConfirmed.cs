namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order confirmed email template (sent to customer)
    /// </summary>
    public static class OrderConfirmed
    {
        public static string Subject => "Order Confirmed - Rumi Restaurant";

        public static string GetHtmlBody(string customerName, string orderNumber, string orderType, int estimatedPreparationMinutes, string contactEmail)
        {
            var email = contactEmail;
            var orderTypeEmoji = orderType switch
            {
                "DineIn" => "🍽️ Dine In",
                "Takeaway" => "🛍️ Takeaway",
                "Delivery" => "🚚 Delivery",
                _ => orderType
            };

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Order Confirmed</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #27ae60; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #27ae60; }}
        .order-number {{ background: #27ae60; color: white; padding: 15px; border-radius: 5px; text-align: center; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .confirmed {{ background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 20px 0; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ Rumi Restaurant</h1>
        </div>
        <div class='content'>
            <div class='confirmed'>
                <h2 style='margin: 0; color: #27ae60;'>✅ Order Confirmed!</h2>
            </div>

            <p>Dear {customerName},</p>
            <p>Great news! Your order <strong>#{orderNumber}</strong> has been confirmed and is being prepared.</p>

            <div class='info-box'>
                <strong>📦 Order Type:</strong> {orderTypeEmoji}<br>
                <strong>⏱️ Estimated Preparation Time:</strong> {estimatedPreparationMinutes} minutes
            </div>

            <p>We will do our best to have your order ready as soon as possible.</p>

            <p>If you have any questions, please contact us at {email}</p>
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

        public static string GetTextBody(string customerName, string orderNumber, string orderType, int estimatedPreparationMinutes, string contactEmail)
        {
            var email = contactEmail;
            var orderTypeText = orderType switch
            {
                "DineIn" => "Dine In",
                "Takeaway" => "Takeaway",
                "Delivery" => "Delivery",
                _ => orderType
            };

            return $@"Rumi Restaurant - Order Confirmed

✅ ORDER CONFIRMED!

Dear {customerName},

Great news! Your order #{orderNumber} has been confirmed and is being prepared.

Order Type: {orderTypeText}
Estimated Preparation Time: {estimatedPreparationMinutes} minutes

We will do our best to have your order ready as soon as possible.

If you have any questions, please contact us at {email}

We look forward to serving you!

Best regards,
Rumi Restaurant Team

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
