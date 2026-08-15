using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Password changed notification template
    /// </summary>
    public static class PasswordChanged
    {
        private const string Set = "PasswordChanged";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, DateTimeOffset changedAt)
        {
            var t = EmailText.For(culture, Set);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{t["PageTitle"]}</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #e67e22; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .alert {{ background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔐 {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>{t["Heading"]}</h2>
            <p>{t.Format("HelloFullName", EmailHtml.Encode(firstName), EmailHtml.Encode(lastName))}</p>
            <div class='alert'>
                <strong>✅ {t["Changed"]}</strong><br>
                {t["ChangedOnLabel"]} {LongDateTime(changedAt, culture)}
            </div>
            <p>{t["NoAction"]}</p>
            <p><strong>{t["IfNotYouLabel"]}</strong></p>
            <ul>
                <li>{t["IfNotYou1"]}</li>
                <li>{t["IfNotYou2"]}</li>
                <li>{t["IfNotYou3"]}</li>
            </ul>
            <p>{t["Advice"]}</p>
            <p>{t["BestRegards"]}<br>{t.Format("TheBrandTeam", brand.Name)}</p>
        </div>
        <div class='footer'>
            <p>{t["AutomatedMessage"]}</p>
            <p>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, DateTimeOffset changedAt)
        {
            var t = EmailText.For(culture, Set);

            return $@"{brand.Name} - {t["PageTitle"]}

{t.Format("HelloFullName", firstName, lastName)}

{t.Format("ChangedOn", LongDateTime(changedAt, culture))}

{t["NoAction"]}

{t["IfNotYouLabel"]}
- {t["IfNotYou1"]}
- {t["IfNotYou2"]}
- {t["IfNotYou3"]}

{t["Advice"]}

{t["BestRegards"]}
{t.Format("TheBrandTeam", brand.Name)}

{t["AutomatedMessage"]}
{Copyright(t, brand)}";
        }
    }
}
