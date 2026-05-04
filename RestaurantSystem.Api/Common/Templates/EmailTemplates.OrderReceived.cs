namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order received email template (sent to customer)
    /// </summary>
    public static class OrderReceived
    {
        public static string Subject => "Order Received - Rumi Restaurant";

        public static string GetHtmlBody(string customerName, string orderNumber, string orderType, decimal total,
            IEnumerable<(string name, int quantity, decimal price)> items, string contactEmail,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var email = contactEmail;
            var itemsSection = string.Join("", items.Select(item =>
                $@"<tr>
                    <td style='padding: 10px; border-bottom: 1px solid #eee;'>{item.name}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: center;'>x{item.quantity}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>CHF {item.price:F2}</td>
                </tr>"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"<div class='info-box'>
                        <strong>Special Instructions:</strong><br>
                        {specialInstructions}
                    </div>";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"<div class='info-box'>
                        <strong>📍 Delivery Address:</strong><br>
                        {deliveryAddress}
                    </div>";

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
    <title>Order Received</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #d4af37; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #d4af37; }}
        .order-number {{ background: linear-gradient(135deg, #d4af37 0%, #f4c430 100%); color: white; padding: 20px; border-radius: 5px; text-align: center; margin: 20px 0; }}
        .order-number-value {{ font-size: 28px; font-weight: bold; letter-spacing: 2px; }}
        .order-number-label {{ font-size: 14px; opacity: 0.9; margin-top: 5px; }}
        table {{ width: 100%; border-collapse: collapse; margin: 20px 0; background: white; }}
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
            <h2>Order Received</h2>
            <p>Dear {customerName},</p>
            <p>Thank you for your order at Rumi Restaurant! We have received your order details.</p>

            <div class='order-number'>
                <div class='order-number-label'>ORDER NUMBER</div>
                <div class='order-number-value'>{orderNumber}</div>
            </div>

            <div class='pending'>
                <strong>⏳ Pending Confirmation</strong><br>
                Your order is currently pending confirmation. We will notify you as soon as the restaurant confirms your order.
            </div>

            <div class='info-box'>
                <strong>📦 Order Type:</strong> {orderTypeEmoji}<br>
                <strong>💰 Total Amount:</strong> CHF {total:F2}
            </div>

            <h3>Order Items:</h3>
            <table>
                <thead>
                    <tr style='background: #f5f5f5;'>
                        <th style='padding: 10px; text-align: left;'>Item</th>
                        <th style='padding: 10px; text-align: center;'>Qty</th>
                        <th style='padding: 10px; text-align: right;'>Price</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsSection}
                </tbody>
            </table>

            {deliverySection}
            {instructionsSection}

            <p>You can track your order status in your account. If you have any questions, please contact us at {email}</p>
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

        public static string GetTextBody(string customerName, string orderNumber, string orderType, decimal total,
            IEnumerable<(string name, int quantity, decimal price)> items, string contactEmail,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var email = contactEmail;
            var itemsSection = string.Join("\n", items.Select(item =>
                $"{item.name} x{item.quantity} = CHF {item.price:F2}"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"

Special Instructions:
{specialInstructions}";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"

Delivery Address:
{deliveryAddress}";

            var orderTypeText = orderType switch
            {
                "DineIn" => "Dine In",
                "Takeaway" => "Takeaway",
                "Delivery" => "Delivery",
                _ => orderType
            };

            return $@"Rumi Restaurant - Order Received

ORDER RECEIVED

Dear {customerName},

Thank you for your order at Rumi Restaurant! We have received your order details.

ORDER NUMBER: {orderNumber}

PENDING CONFIRMATION
Your order is currently pending confirmation. We will notify you as soon as the restaurant confirms your order.

Order Type: {orderTypeText}
Total Amount: CHF {total:F2}

Order Items:
{itemsSection}{deliverySection}{instructionsSection}

You can track your order status in your account. If you have any questions, please contact us at {email}

We look forward to serving you!

Best regards,
Rumi Restaurant Team

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
