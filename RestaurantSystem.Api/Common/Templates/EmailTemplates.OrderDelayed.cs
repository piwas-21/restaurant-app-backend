namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order delayed email template (sent to customer)
    /// </summary>
    public static class OrderDelayed
    {
        public static string Subject => "Action Required: Order Delay - Rumi Restaurant";

        public static string GetHtmlBody(string customerName, string orderNumber, int delayMinutes, string approveUrl, string rejectUrl, string contactEmail)
        {
            var email = contactEmail;
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Order Delayed</title>
    <meta name='color-scheme' content='light dark'>
    <meta name='supported-color-schemes' content='light dark'>
    <style>
        :root {{
            --primary-color: #f59e0b;
            --success-color: #10b981;
            --danger-color: #ef4444;
            --text-color: #1f2937;
            --bg-color: #f3f4f6;
            --card-bg: #ffffff;
        }}

        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #1f2937;
            background-color: #f3f4f6;
            margin: 0;
            padding: 0;
        }}

        .container {{
            max-width: 600px;
            margin: 20px auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
        }}

        .header {{
            background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
            color: white;
            padding: 30px 20px;
            text-align: center;
        }}

        .header h1 {{
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            text-shadow: 0 1px 2px rgba(0,0,0,0.1);
        }}

        .content {{
            padding: 40px 30px;
            background: #ffffff;
        }}

        .status-icon {{
            font-size: 48px;
            text-align: center;
            margin-bottom: 20px;
        }}

        .message-title {{
            text-align: center;
            font-size: 20px;
            font-weight: 600;
            color: #d97706;
            margin-bottom: 20px;
        }}

        .info-card {{
            background: #fffbeb;
            border: 1px solid #fcd34d;
            border-radius: 8px;
            padding: 20px;
            margin: 25px 0;
            text-align: center;
        }}

        .time-display {{
            font-size: 32px;
            font-weight: 800;
            color: #92400e;
            margin: 10px 0;
        }}

        .order-details {{
            text-align: center;
            color: #6b7280;
            font-size: 14px;
            margin-bottom: 30px;
            border-bottom: 1px solid #e5e7eb;
            padding-bottom: 20px;
        }}

        .actions {{
            text-align: center;
            margin: 35px 0;
            display: flex;
            flex-direction: column;
            gap: 15px;
            align-items: center;
        }}

        .btn {{
            display: inline-block;
            padding: 16px 32px;
            text-decoration: none;
            border-radius: 8px;
            font-weight: 600;
            font-size: 16px;
            transition: transform 0.1s;
            text-align: center;
            min-width: 200px;
        }}

        .btn-accept {{
            background-color: #10b981;
            color: #ffffff !important;
            box-shadow: 0 4px 6px -1px rgba(16, 185, 129, 0.3);
            border: 1px solid #059669;
        }}

        .btn-reject {{
            background-color: #ffffff;
            color: #ef4444 !important;
            border: 2px solid #ef4444;
        }}

        .footer {{
            background-color: #f9fafb;
            padding: 20px;
            text-align: center;
            color: #6b7280;
            font-size: 12px;
            border-top: 1px solid #e5e7eb;
        }}

        @media (min-width: 480px) {{
            .actions {{
                flex-direction: row;
                justify-content: center;
            }}
        }}

        @media (prefers-color-scheme: dark) {{
            body {{ background-color: #111827; color: #e5e7eb; }}
            .container {{ background-color: #1f2937; }}
            .content {{ background-color: #1f2937; }}
            .info-card {{ background-color: #374151; border-color: #4b5563; }}
            .time-display {{ color: #fbbf24; }}
            .btn-reject {{ background-color: #1f2937; border-color: #ef4444; }}
            .footer {{ background-color: #111827; border-color: #374151; color: #9ca3af; }}
            .order-details {{ border-color: #374151; color: #9ca3af; }}
            .header h1 {{ color: #ffffff; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ Rumi Restaurant</h1>
        </div>
        <div class='content'>
            <div class='status-icon'>⏳</div>
            <div class='message-title'>Update on Your Order</div>

            <p>Dear {customerName},</p>
            <p>Thank you for choosing Rumi Restaurant! We are currently experiencing high demand, and we want to ensure your meal meets our quality standards.</p>

            <div class='order-details'>
                Order #{orderNumber}
            </div>

            <div class='info-card'>
                <div>New Estimated Preparation Time</div>
                <div class='time-display'>{delayMinutes} Minutes</div>
                <div>Would you like to proceed?</div>
            </div>

            <div class='actions'>
                <a href='{approveUrl}' class='btn btn-accept'>
                    ✅ Accept Delay
                </a>
                <a href='{rejectUrl}' class='btn btn-reject'>
                    ❌ Cancel Order
                </a>
            </div>

            <p style='font-size: 0.9em; color: #6b7280; text-align: center;'>
                If you choose to cancel, you will not be charged.
            </p>
        </div>
        <div class='footer'>
            <p>Rumi Restaurant | Geneva | +41 22 786 33 33</p>
            <p>© 2024 Rumi Restaurant. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(string customerName, string orderNumber, int delayMinutes, string approveUrl, string rejectUrl, string contactEmail)
        {
            var email = contactEmail;
            return $@"Rumi Restaurant - Action Required: Order Delay

Dear {customerName},

Thank you for your order. Due to high demand, we need a bit more time to prepare your delicious meal.

Order Number: {orderNumber}

Proposed Preparation Time:
We estimate your order will be ready in approximately {delayMinutes} minutes.

Please let us know if this works for you:

Accept Delay: {approveUrl}

Cancel Order: {rejectUrl}

If you choose to cancel, you will not be charged.

Best regards,
Rumi Restaurant Team

Rumi Restaurant | Geneva | {email}
© 2024 Rumi Restaurant. All rights reserved.";
        }
    }
}
