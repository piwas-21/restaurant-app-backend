using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order confirmed email template (sent to customer)
    /// </summary>
    public static class OrderConfirmed
    {
        private const string Set = "OrderConfirmed";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string orderType, int estimatedPreparationMinutes, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var orderTypeEmoji = OrderTypeLabel(t, orderType, withEmoji: true);
            var minutes = estimatedPreparationMinutes.ToString(CultureInfo.InvariantCulture);

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
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <div class='confirmed'>
                <h2 style='margin: 0; color: #27ae60;'>✅ {t["Confirmed"]}</h2>
            </div>

            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t.Format("GoodNews", $"<strong>#{orderNumber}</strong>")}</p>

            <div class='info-box'>
                <strong>📦 {t["OrderTypeLabel"]}</strong> {orderTypeEmoji}<br>
                <strong>⏱️ {t["PreparationLabel"]}</strong> {t.Format("Minutes", minutes)}
            </div>

            <p>{t["BestEffort"]}</p>

            <p>{t.Format("Questions", email)}</p>
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

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string orderType, int estimatedPreparationMinutes, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var orderTypeText = OrderTypeLabel(t, orderType);
            var minutes = estimatedPreparationMinutes.ToString(CultureInfo.InvariantCulture);

            return $@"{brand.Name} - {t["Heading"]}

✅ {t["ConfirmedUpper"]}

{Greeting(t, "Dear", customerName)}

{t.Format("GoodNews", $"#{orderNumber}")}

{t["OrderTypeLabel"]} {orderTypeText}
{t["PreparationLabel"]} {t.Format("Minutes", minutes)}

{t["BestEffort"]}

{t.Format("Questions", email)}

{t["LookForward"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
