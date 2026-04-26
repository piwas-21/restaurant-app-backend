namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order confirmation email template (sent to admin/restaurant)
    /// </summary>
    public static class OrderConfirmationAdmin
    {
        public static string Subject => "New Order - Rumi Restaurant";

        public static string GetHtmlBody(string orderNumber, string customerName, string customerEmail, string customerPhone,
            string orderType, decimal total, IEnumerable<(string name, int quantity, decimal price)> items,
            string baseUrl, string frontendBaseUrl, string contactEmail,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var email = contactEmail;
            var apiBaseUrl = baseUrl;
            var frontendUrl = frontendBaseUrl;
            var itemsSection = string.Join("", items.Select(item =>
                $@"<tr>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb;'>{item.name}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: center;'>×{item.quantity}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right; font-weight: 600;'>CHF {item.price:F2}</td>
                </tr>"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"<div style='background: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #92400e; font-size: 14px;'>📝 Special Instructions:</strong><br>
                        <span style='color: #78350f; margin-top: 8px; display: block;'>{specialInstructions}</span>
                    </div>";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"<div style='background: #dbeafe; border-left: 4px solid #3b82f6; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #1e40af; font-size: 14px;'>📍 Delivery Address:</strong><br>
                        <span style='color: #1e3a8a; margin-top: 8px; display: block; white-space: pre-line;'>{deliveryAddress}</span>
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
    <meta name='color-scheme' content='light dark'>
    <title>New Order</title>
    <style>
        @media (prefers-color-scheme: dark) {{
            .light-only {{ display: none !important; }}
            .dark-only {{ display: block !important; }}
        }}
        @media (prefers-color-scheme: light) {{
            .dark-only {{ display: none !important; }}
            .light-only {{ display: block !important; }}
        }}
    </style>
</head>
<body style='margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; line-height: 1.6; background-color: #f3f4f6;'>
    <!-- Light Mode Version -->
    <div class='light-only' style='max-width: 600px; margin: 0 auto; background: #ffffff;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #d4af37 0%, #f4c430 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ Rumi Restaurant</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>New Order Notification</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Order Number Badge -->
            <div style='background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(16, 185, 129, 0.2);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>Order Number</div>
                <div style='font-size: 32px; font-weight: 700; letter-spacing: 2px;'>{orderNumber}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>👤 Customer Information</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>Name:</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{customerName}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>Email:</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{customerEmail}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>Phone:</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{customerPhone}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Details -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>📦 Order Details</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>Type:</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{orderTypeEmoji}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>Total:</td>
                        <td style='padding: 6px 0; color: #059669; font-size: 18px; font-weight: 700;'>CHF {total:F2}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Items -->
            <h3 style='margin: 24px 0 12px 0; color: #111827; font-size: 16px; font-weight: 600;'>🛒 Order Items</h3>
            <table style='width: 100%; border-collapse: collapse; background: #ffffff; border: 1px solid #e5e7eb; border-radius: 12px; overflow: hidden;'>
                <thead>
                    <tr style='background: #f9fafb;'>
                        <th style='padding: 12px; text-align: left; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Item</th>
                        <th style='padding: 12px; text-align: center; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Qty</th>
                        <th style='padding: 12px; text-align: right; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Price</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsSection}
                </tbody>
            </table>

            {deliverySection}
            {instructionsSection}

            <!-- Action Required Alert -->
            <div style='background: #fef3c7; border: 2px solid #fbbf24; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: #92400e; font-size: 16px; display: block; margin-bottom: 4px;'>Action Required</strong>
                <p style='margin: 0; color: #78350f; font-size: 14px;'>Please confirm or cancel this order</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=0' style='display: inline-block; background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px rgba(16, 185, 129, 0.3); margin: 0 8px 12px 8px;'>✓ Confirm Now</a>
            </div>

            <p style='text-align: center; margin: 20px 0; color: #6b7280; font-size: 14px; margin-bottom: 12px; font-weight: 600;'>Or confirm with preparation time:</p>

            <div style='text-align: center; margin: 20px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=15' style='display: inline-block; background: #7fa89bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>15 min</a>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=30' style='display: inline-block; background: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>30 min</a>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=45' style='display: inline-block; background: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>45 min</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-cancel' style='display: inline-block; background: #dc2626; color: white; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.3);'>✕ Cancel Order</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #f3f4f6; border-radius: 8px; font-size: 13px; color: #6b7280;'>
                Need a different time? <a href='{frontendUrl}/admin/orders-management' style='color: #3b82f6; text-decoration: none; font-weight: 600;'>Open dashboard</a> for custom preparation time
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #e5e7eb;'>
                <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 14px;'>The customer will be notified automatically after you take action.</p>
                <p style='margin: 0; color: #111827; font-size: 14px;'><strong>Best regards,</strong><br>Restaurant System</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #f9fafb; padding: 24px; text-align: center; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 13px;'><strong>Rumi Restaurant</strong> | Geneva | {email}</p>
            <p style='margin: 0; color: #9ca3af; font-size: 12px;'>© 2024 Rumi Restaurant. All rights reserved.</p>
        </div>
    </div>

    <!-- Dark Mode Version -->
    <div class='dark-only' style='max-width: 600px; margin: 0 auto; background: #1f2937;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #b8941f 0%, #d4af37 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ Rumi Restaurant</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>New Order Notification</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Order Number Badge -->
            <div style='background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(5, 150, 105, 0.3);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>Order Number</div>
                <div style='font-size: 32px; font-weight: 700; letter-spacing: 2px;'>{orderNumber}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>👤 Customer Information</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>Name:</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{customerName}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>Email:</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{customerEmail}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>Phone:</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{customerPhone}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Details -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>📦 Order Details</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>Type:</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{orderTypeEmoji}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>Total:</td>
                        <td style='padding: 6px 0; color: #34d399; font-size: 18px; font-weight: 700;'>CHF {total:F2}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Items -->
            <h3 style='margin: 24px 0 12px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>🛒 Order Items</h3>
            <table style='width: 100%; border-collapse: collapse; background: #374151; border: 1px solid #4b5563; border-radius: 12px; overflow: hidden;'>
                <thead>
                    <tr style='background: #4b5563;'>
                        <th style='padding: 12px; text-align: left; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Item</th>
                        <th style='padding: 12px; text-align: center; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Qty</th>
                        <th style='padding: 12px; text-align: right; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Price</th>
                    </tr>
                </thead>
                <tbody style='color: #e5e7eb;'>
                    {itemsSection}
                </tbody>
            </table>

            {deliverySection}
            {instructionsSection}

            <!-- Action Required Alert -->
            <div style='background: #78350f; border: 2px solid #f59e0b; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: #fef3c7; font-size: 16px; display: block; margin-bottom: 4px;'>Action Required</strong>
                <p style='margin: 0; color: #fde68a; font-size: 14px;'>Please confirm or cancel this order</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=0' style='display: inline-block; background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px rgba(5, 150, 105, 0.4); margin: 0 8px 12px 8px;'>✓ Confirm Now</a>
            </div>

            <p style='text-align: center; margin: 20px 0 12px 0; color: #9ca3af; font-size: 14px; font-weight: 500;'>Or confirm with preparation time:</p>

            <div style='text-align: center; margin: 12px 0 24px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=15' style='display: inline-block; background: #6b9688; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>15 min</a>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=30' style='display: inline-block; background: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>30 min</a>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-confirm?minutes=45' style='display: inline-block; background: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>45 min</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Orders/{orderNumber}/quick-cancel' style='display: inline-block; background: #dc2626; color: white; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.4);'>✕ Cancel Order</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #374151; border-radius: 8px; font-size: 13px; color: #9ca3af;'>
                Need a different time? <a href='{frontendUrl}/admin/orders-management' style='color: #60a5fa; text-decoration: none; font-weight: 600;'>Open dashboard</a> for custom preparation time
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #4b5563;'>
                <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 14px;'>The customer will be notified automatically after you take action.</p>
                <p style='margin: 0; color: #f9fafb; font-size: 14px;'><strong>Best regards,</strong><br>Restaurant System</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #374151; padding: 24px; text-align: center; border-top: 1px solid #4b5563;'>
            <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 13px;'><strong>Rumi Restaurant</strong> | Geneva | {email}</p>
            <p style='margin: 0; color: #6b7280; font-size: 12px;'>© 2024 Rumi Restaurant. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(string orderNumber, string customerName, string customerEmail, string customerPhone,
            string orderType, decimal total, IEnumerable<(string name, int quantity, decimal price)> items,
            string contactEmail,
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

            return $@"Rumi Restaurant - New Order

📦 NEW ORDER RECEIVED

Order Number: {orderNumber}

Customer: {customerName}
Email: {customerEmail}
Phone: {customerPhone}

Order Type: {orderTypeText}
Total Amount: CHF {total:F2}

Order Items:
{itemsSection}{deliverySection}{instructionsSection}

ACTION REQUIRED:
Please prepare this order for {(orderType == "Delivery" ? "delivery" : orderType == "Takeaway" ? "takeaway" : "serving")}.

Log in to your admin dashboard to manage this order.

Best regards,
Restaurant System

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
