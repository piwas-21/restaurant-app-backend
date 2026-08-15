using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Order cancellation email template (sent to customer)
    /// </summary>
    public static class OrderCancelled
    {
        private const string Set = "OrderCancelled";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string cancellationReason, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
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
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>{t["Heading"]}</h2>
            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t["Regret"]}</p>

            <div class='order-number'>
                <div class='order-number-label'>{t["OrderNumberLabelUpper"]}</div>
                <div class='order-number-value'>{orderNumber}</div>
            </div>

            <div class='info-box'>
                <strong>{t["ReasonLabel"]}</strong><br>
                {EmailHtml.Encode(cancellationReason)}
            </div>

            <p>{t.Format("Questions", email)}</p>
            <p>{t["Apology"]}</p>
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

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string customerName, string orderNumber, string cancellationReason, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            return $@"{brand.Name} - {t["Heading"]}

{Greeting(t, "Dear", customerName)}

{t["Regret"]}

{t["OrderNumberLabel"]} {orderNumber}

{t["ReasonLabel"]}
{cancellationReason}

{t.Format("Questions", email)}

{t["Apology"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
