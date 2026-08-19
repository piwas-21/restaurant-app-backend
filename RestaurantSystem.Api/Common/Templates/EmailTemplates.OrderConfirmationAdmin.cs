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

        /// <summary>
        /// The three places where the light and dark blocks differ by something that is NOT a
        /// colour. Nothing recorded why, and both are preserved exactly as they were — the point of
        /// naming them is that a drift buried in two 100-line copies is now three lines you can read
        /// side by side and decide about (#356).
        /// </summary>
        /// <param name="ItemsTableBodyAttributes">Dark tints the items table body; light does not.</param>
        /// <param name="ConfirmWithTimeHintStyle">Dark spaces and weights the hint differently.</param>
        /// <param name="TimeButtonRowStyle">…and the button row under it.</param>
        private sealed record ModeQuirks(
            string ItemsTableBodyAttributes,
            string ConfirmWithTimeHintStyle,
            string TimeButtonRowStyle)
        {
            public static readonly ModeQuirks Light = new(
                "",
                "text-align: center; margin: 20px 0; color: #6b7280; font-size: 14px; margin-bottom: 12px; font-weight: 600;",
                "text-align: center; margin: 20px 0;");

            public static readonly ModeQuirks Dark = new(
                " style='color: #e5e7eb;'",
                "text-align: center; margin: 20px 0 12px 0; color: #9ca3af; font-size: 14px; font-weight: 500;",
                "text-align: center; margin: 12px 0 24px 0;");
        }


        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        /// <param name="quickActionToken">
        /// The order's <c>QuickActionToken</c> — the bearer secret that authorises the anonymous
        /// confirm/cancel endpoints (ORDER-TYPE-AVAILABILITY-PLAN §9.20). Null only for orders
        /// created before that column existed; their buttons render and then land on
        /// "Order Not Found", which is the intended outcome — the owner uses the dashboard link.
        /// </param>
        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, OrderMailDetails order, EmailLinks links)
        {
            var t = EmailText.For(culture, Set);
            var (customerName, customerEmail, customerPhone) = guest;
            var (orderNumber, orderType, total, items, currency, quickActionToken, specialInstructions, deliveryAddress) = order;
            var (apiBaseUrl, frontendUrl, email) = links;

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

            // ONE block, rendered twice. The two used to be written out in full — 100 lines each,
            // differing only in colour — which is the duplication `sonar.cpd.exclusions` was hiding
            // (#356). A local function rather than a method: it reads the dozen locals above.
            string Block(EmailPalette p, ModeQuirks q) => $@"<!-- {p.ModeName} Mode Version -->
    <div class='{p.ModeClass}' style='max-width: 600px; margin: 0 auto; background: {p.PageBackground};'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, {p.HeaderGradientFrom} 0%, {p.HeaderGradientTo} 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t["Notification"]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Order Number Badge -->
            <div style='background: linear-gradient(135deg, {p.ConfirmGradientFrom} 0%, {p.ConfirmGradientTo} 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px {p.OrderBadgeShadow};'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["OrderNumberLabel"]}</div>
                <div style='font-size: 32px; font-weight: 700; letter-spacing: 2px;'>{orderNumber}</div>
            </div>

            <!-- Customer Info -->
            {AdminCustomerCard(t, p, customerName, customerEmail, customerPhone)}

            <!-- Order Details -->
            <div style='background: {p.SurfaceBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: {p.StrongText}; font-size: 16px; font-weight: 600;'>📦 {t["OrderDetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px; width: 80px;'>{t["TypeLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{orderTypeEmoji}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["TotalLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.TotalText}; font-size: 18px; font-weight: 700;'>{currency} {total:F2}</td>
                    </tr>
                </table>
            </div>

            <!-- Order Items -->
            <h3 style='margin: 24px 0 12px 0; color: {p.StrongText}; font-size: 16px; font-weight: 600;'>🛒 {t["ItemsTitle"]}</h3>
            <table style='width: 100%; border-collapse: collapse; background: {p.TableBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; overflow: hidden;'>
                <thead>
                    <tr style='background: {p.TableHeadBackground};'>
                        <th style='padding: 12px; text-align: left; color: {p.TableHeadText}; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnItem"]}</th>
                        <th style='padding: 12px; text-align: center; color: {p.TableHeadText}; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnQty"]}</th>
                        <th style='padding: 12px; text-align: right; color: {p.TableHeadText}; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>{t["ColumnPrice"]}</th>
                    </tr>
                </thead>
                <tbody{q.ItemsTableBodyAttributes}>
                    {itemsSection}
                </tbody>
            </table>

            {deliverySection}
            {instructionsSection}

            <!-- Action Required Alert -->
            <div style='background: {p.NoticeBackground}; border: 2px solid {p.NoticeBorder}; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: {p.NoticeHeading}; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: {p.NoticeText}; font-size: 14px;'>{t["ConfirmOrCancel"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{confirmUrl}0' style='display: inline-block; background: linear-gradient(135deg, {p.ConfirmGradientFrom} 0%, {p.ConfirmGradientTo} 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 4px 6px {p.ConfirmButtonShadow}; margin: 0 8px 12px 8px;'>✓ {t["ConfirmNow"]}</a>
            </div>

            <p style='{q.ConfirmWithTimeHintStyle}'>{t["OrConfirmWithTime"]}</p>

            <div style='{q.TimeButtonRowStyle}'>
                <a href='{confirmUrl}15' style='display: inline-block; background: {p.TimeButtonBackground}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 15)}</a>
                <a href='{confirmUrl}30' style='display: inline-block; background: {p.DashboardButtonBackground}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 30)}</a>
                <a href='{confirmUrl}45' style='display: inline-block; background: {p.DashboardButtonBackground}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; margin: 4px 6px; min-width: 90px;'>{t.Format(MinutesShortKey, 45)}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{cancelUrl}' style='display: inline-block; background: #dc2626; color: white; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 14px; box-shadow: 0 2px 4px {p.CancelButtonShadow};'>✕ {t["CancelOrder"]}</a>
            </div>

            {AdminFooter(t, p, brand, email, frontendUrl, "/admin/orders-management")}
    </div>";

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
    {Block(EmailPalette.Light, ModeQuirks.Light)}

    {Block(EmailPalette.Dark, ModeQuirks.Dark)}
</body>
</html>";
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, OrderMailDetails order, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var (customerName, customerEmail, customerPhone) = guest;
            var (orderNumber, orderType, total, items, currency, _, specialInstructions, deliveryAddress) = order;
            var (itemsSection, instructionsSection, deliverySection) =
                OrderTextSections(t, items, currency, specialInstructions, deliveryAddress);


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
