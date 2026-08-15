using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order received email template (sent to customer)
    /// </summary>
    public static class OrderReceived
    {
        private const string Set = "OrderReceived";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string orderType, decimal total,
            string currency, IEnumerable<(string name, int quantity, decimal price)> items, string contactEmail,
            string? specialInstructions = null, string? deliveryAddress = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var itemsSection = string.Join("", items.Select(item =>
                $@"<tr>
                    <td style='padding: 10px; border-bottom: 1px solid #eee;'>{EmailHtml.Encode(item.name)}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: center;'>x{item.quantity}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>{currency} {item.price:F2}</td>
                </tr>"));

            var instructionsSection = string.IsNullOrEmpty(specialInstructions)
                ? ""
                : $@"<div class='info-box'>
                        <strong>{t["InstructionsLabel"]}</strong><br>
                        {EmailHtml.Encode(specialInstructions)}
                    </div>";

            var deliverySection = string.IsNullOrEmpty(deliveryAddress)
                ? ""
                : $@"<div class='info-box'>
                        <strong>📍 {t["DeliveryLabel"]}</strong><br>
                        {EmailHtml.Encode(deliveryAddress)}
                    </div>";

            var orderTypeEmoji = OrderTypeLabel(t, orderType, withEmoji: true);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{t["Heading"]}</title>
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
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>{t["Heading"]}</h2>
            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t.Format("ThankYou", brand.Name)}</p>

            <div class='order-number'>
                <div class='order-number-label'>{t["OrderNumberLabel"]}</div>
                <div class='order-number-value'>{orderNumber}</div>
            </div>

            <div class='pending'>
                <strong>⏳ {t["PendingTitle"]}</strong><br>
                {t["PendingBody"]}
            </div>

            <div class='info-box'>
                <strong>📦 {t["OrderTypeLabel"]}</strong> {orderTypeEmoji}<br>
                <strong>💰 {t["TotalLabel"]}</strong> {currency} {total:F2}
            </div>

            <h3>{t["ItemsLabel"]}</h3>
            <table>
                <thead>
                    <tr style='background: #f5f5f5;'>
                        <th style='padding: 10px; text-align: left;'>{t["ColumnItem"]}</th>
                        <th style='padding: 10px; text-align: center;'>{t["ColumnQty"]}</th>
                        <th style='padding: 10px; text-align: right;'>{t["ColumnPrice"]}</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsSection}
                </tbody>
            </table>

            {deliverySection}
            {instructionsSection}

            <p>{t.Format("Track", email)}</p>
            <p>{t["LookForward"]}</p>
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>
        </div>
        <div class='footer'>
            <p>{brand.Name} | {brand.City} | {email}</p>
            <p>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string orderType, decimal total,
            string currency, IEnumerable<(string name, int quantity, decimal price)> items, string contactEmail,
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

            return $@"{brand.Name} - {t["Heading"]}

{t["HeadingUpper"]}

{Greeting(t, "Dear", customerName)}

{t.Format("ThankYou", brand.Name)}

{Labelled(t, t["OrderNumberLabel"], orderNumber)}

{t["PendingTitleUpper"]}
{t["PendingBody"]}

{t["OrderTypeLabel"]} {orderTypeText}
{t["TotalLabel"]} {currency} {total:F2}

{t["ItemsLabel"]}
{itemsSection}{deliverySection}{instructionsSection}

{t.Format("Track", email)}

{t["LookForward"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
