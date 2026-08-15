using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order confirmation email template (sent to admin/restaurant)
    /// </summary>
    public static class OrderConfirmationAdmin
    {
        private const string Set = "OrderConfirmationAdmin";

        /// <summary>"{0} min" — one preparation-time button, rendered three times per colour scheme.</summary>
        private const string MinutesShortKey = "MinutesShort";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        /// <param name="quickActionToken">
        /// The order's <c>QuickActionToken</c> — the bearer secret that authorises the anonymous
        /// confirm/cancel endpoints (ORDER-TYPE-AVAILABILITY-PLAN §9.20). Null only for orders
        /// created before that column existed; their buttons render and then land on
        /// "Order Not Found", which is the intended outcome — the owner uses the dashboard link.
        /// </param>
        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string orderNumber, string customerName, string customerEmail, string customerPhone,
            string orderType, decimal total, string currency, IEnumerable<(string name, int quantity, decimal price)> items,
            string baseUrl, string frontendBaseUrl, string contactEmail, string? quickActionToken,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var apiBaseUrl = baseUrl;
            var frontendUrl = frontendBaseUrl;

            // The light-mode and dark-mode blocks below repeat the same five action links, so the
            // URLs are built once here rather than eight times inline. "&amp;" not "&": these sit
            // in an href attribute and a bare ampersand is invalid HTML.
            var linkBase = $"{apiBaseUrl}/api/Orders/{Uri.EscapeDataString(orderNumber)}";
            var tokenParam = $"token={Uri.EscapeDataString(quickActionToken ?? string.Empty)}";
            var confirmUrl = $"{linkBase}/quick-confirm?{tokenParam}&amp;minutes=";
            var cancelUrl = $"{linkBase}/quick-cancel?{tokenParam}";
            var itemsSection = string.Join("", items.Select(item =>
                $@"<tr>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb;'>{EmailHtml.Encode(item.name)}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: center;'>×{item.quantity}</td>
                    <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right; font-weight: 600;'>{currency} {item.price:F2}</td>
                </tr>"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"<div style='background: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #92400e; font-size: 14px;'>📝 {t["InstructionsLabel"]}</strong><br>
                        <span style='color: #78350f; margin-top: 8px; display: block;'>{EmailHtml.Encode(specialInstructions)}</span>
                    </div>";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"<div style='background: #dbeafe; border-left: 4px solid #3b82f6; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #1e40af; font-size: 14px;'>📍 {t["DeliveryLabel"]}</strong><br>
                        <span style='color: #1e3a8a; margin-top: 8px; display: block; white-space: pre-line;'>{EmailHtml.Encode(deliveryAddress)}</span>
                    </div>";

            var orderTypeEmoji = OrderTypeLabel(t, orderType, withEmoji: true);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta name='color-scheme' content='light dark'>
    <title>{t["Heading"]}</title>
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
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t["Notification"]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Order Number Badge -->
            <div style='background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(16, 185, 129, 0.2);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["OrderNumberLabel"]}</div>
                <div style='font-size: 32px; font-weight: 700; letter-spacing: 2px;'>{orderNumber}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>👤 {t["CustomerInfoTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>{t["NameLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(customerName)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["EmailLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{EmailHtml.Encode(customerEmail)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["PhoneLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{EmailHtml.Encode(customerPhone)}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Details -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>📦 {t["OrderDetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>{t["TypeLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{orderTypeEmoji}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["TotalLabel"]}</td>
                        <td style='padding: 6px 0; color: #059669; font-size: 18px; font-weight: 700;'>{currency} {total:F2}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Items -->
            <h3 style='margin: 24px 0 12px 0; color: #111827; font-size: 16px; font-weight: 600;'>🛒 {t["ItemsTitle"]}</h3>
            <table style='width: 100%; border-collapse: collapse; background: #ffffff; border: 1px solid #e5e7eb; border-radius: 12px; overflow: hidden;'>
                <thead>
                    <tr style='background: #f9fafb;'>
                        <th style='padding: 12px; text-align: left; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnItem"]}</th>
                        <th style='padding: 12px; text-align: center; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnQty"]}</th>
                        <th style='padding: 12px; text-align: right; color: #6b7280; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnPrice"]}</th>
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
                <strong style='color: #92400e; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: #78350f; font-size: 14px;'>{t["ConfirmOrCancel"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{confirmUrl}0' style='display: inline-block; background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px rgba(16, 185, 129, 0.3); margin: 0 8px 12px 8px;'>✓ {t["ConfirmNow"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; color: #6b7280; font-size: 14px; margin-bottom: 12px; font-weight: 600;'>{t["OrConfirmWithTime"]}</p>

            <div style='text-align: center; margin: 20px 0;'>
                <a href='{confirmUrl}15' style='display: inline-block; background: #7fa89bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 15)}</a>
                <a href='{confirmUrl}30' style='display: inline-block; background: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 30)}</a>
                <a href='{confirmUrl}45' style='display: inline-block; background: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 45)}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{cancelUrl}' style='display: inline-block; background: #dc2626; color: white; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.3);'>✕ {t["CancelOrder"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #f3f4f6; border-radius: 8px; font-size: 13px; color: #6b7280;'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}/admin/orders-management' style='color: #3b82f6; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #e5e7eb;'>
                <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 14px;'>{t["NotifiedAutomatically"]}</p>
                <p style='margin: 0; color: #111827; font-size: 14px;'><strong>{t["BestRegards"]}</strong><br>{brand.Name}</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #f9fafb; padding: 24px; text-align: center; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 13px;'><strong>{brand.Name}</strong> | {brand.City} | {email}</p>
            <p style='margin: 0; color: #9ca3af; font-size: 12px;'>{Copyright(t, brand)}</p>
        </div>
    </div>

    <!-- Dark Mode Version -->
    <div class='dark-only' style='max-width: 600px; margin: 0 auto; background: #1f2937;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #b8941f 0%, #d4af37 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t["Notification"]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Order Number Badge -->
            <div style='background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(5, 150, 105, 0.3);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["OrderNumberLabel"]}</div>
                <div style='font-size: 32px; font-weight: 700; letter-spacing: 2px;'>{orderNumber}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>👤 {t["CustomerInfoTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>{t["NameLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(customerName)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["EmailLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{EmailHtml.Encode(customerEmail)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["PhoneLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{EmailHtml.Encode(customerPhone)}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Details -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>📦 {t["OrderDetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>{t["TypeLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{orderTypeEmoji}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["TotalLabel"]}</td>
                        <td style='padding: 6px 0; color: #34d399; font-size: 18px; font-weight: 700;'>{currency} {total:F2}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Items -->
            <h3 style='margin: 24px 0 12px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>🛒 {t["ItemsTitle"]}</h3>
            <table style='width: 100%; border-collapse: collapse; background: #374151; border: 1px solid #4b5563; border-radius: 12px; overflow: hidden;'>
                <thead>
                    <tr style='background: #4b5563;'>
                        <th style='padding: 12px; text-align: left; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnItem"]}</th>
                        <th style='padding: 12px; text-align: center; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnQty"]}</th>
                        <th style='padding: 12px; text-align: right; color: #d1d5db; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnPrice"]}</th>
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
                <strong style='color: #fef3c7; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: #fde68a; font-size: 14px;'>{t["ConfirmOrCancel"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{confirmUrl}0' style='display: inline-block; background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px rgba(5, 150, 105, 0.4); margin: 0 8px 12px 8px;'>✓ {t["ConfirmNow"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0 12px 0; color: #9ca3af; font-size: 14px; font-weight: 500;'>{t["OrConfirmWithTime"]}</p>

            <div style='text-align: center; margin: 12px 0 24px 0;'>
                <a href='{confirmUrl}15' style='display: inline-block; background: #6b9688; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 15)}</a>
                <a href='{confirmUrl}30' style='display: inline-block; background: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 30)}</a>
                <a href='{confirmUrl}45' style='display: inline-block; background: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 45)}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{cancelUrl}' style='display: inline-block; background: #dc2626; color: white; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.4);'>✕ {t["CancelOrder"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #374151; border-radius: 8px; font-size: 13px; color: #9ca3af;'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}/admin/orders-management' style='color: #60a5fa; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #4b5563;'>
                <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 14px;'>{t["NotifiedAutomatically"]}</p>
                <p style='margin: 0; color: #f9fafb; font-size: 14px;'><strong>{t["BestRegards"]}</strong><br>{brand.Name}</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #374151; padding: 24px; text-align: center; border-top: 1px solid #4b5563;'>
            <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 13px;'><strong>{brand.Name}</strong> | {brand.City} | {email}</p>
            <p style='margin: 0; color: #6b7280; font-size: 12px;'>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string orderNumber, string customerName, string customerEmail, string customerPhone,
            string orderType, decimal total, string currency, IEnumerable<(string name, int quantity, decimal price)> items,
            string contactEmail,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var itemsSection = string.Join("\n", items.Select(item =>
                $"{item.name} x{item.quantity} = {currency} {item.price:F2}"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"

{t["InstructionsLabel"]}
{specialInstructions}";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"

{t["DeliveryLabel"]}
{deliveryAddress}";

            var orderTypeText = OrderTypeLabel(t, orderType);

            var preparationFor = orderType switch
            {
                "Delivery" => t["PrepareForDelivery"],
                "Takeaway" => t["PrepareForTakeaway"],
                _ => t["PrepareForServing"]
            };

            return $@"{brand.Name} - {t["Heading"]}

📦 {t["HeadingUpper"]}

{Labelled(t, t["OrderNumberLabel"], orderNumber)}

{t["CustomerLabel"]} {customerName}
{t["EmailLabel"]} {customerEmail}
{t["PhoneLabel"]} {customerPhone}

{t["OrderTypeLabel"]} {orderTypeText}
{t["TotalAmountLabel"]} {currency} {total:F2}

{Heading(t, t["ItemsTitle"])}
{itemsSection}{deliverySection}{instructionsSection}

{t["ActionRequiredUpper"]}
{t.Format("PleasePrepare", preparationFor)}

{t["LogIn"]}

{t["BestRegards"]}
{brand.Name}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
