using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Password reset email template
    /// </summary>
    public static class PasswordReset
    {
        private const string Set = "PasswordReset";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string resetUrl, int expirationMinutes = 60)
        {
            var t = EmailText.For(culture, Set);
            var minutes = expirationMinutes.ToString(CultureInfo.InvariantCulture);

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
        .header {{ background: #2c3e50; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .button {{ display: inline-block; padding: 12px 30px; background: #3498db; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .warning {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>{t["Heading"]}</h2>
            <p>{t.Format("HelloFullName", EmailHtml.Encode(firstName), EmailHtml.Encode(lastName))}</p>
            <p>{t.Format("Intro", brand.Name)}</p>
            <p>{t["ClickButton"]}</p>
            <div style='text-align: center;'>
                <a href='{resetUrl}' class='button'>{t["ButtonLabel"]}</a>
            </div>
            <p>{t["CopyLink"]}</p>
            <p style='word-break: break-all;'>{resetUrl}</p>
            <div class='warning'>
                <strong>⚠️ {t["ImportantLabel"]}</strong> {t.Format("Expiry", minutes)}
            </div>
            <p>{t["Questions"]}</p>
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

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string resetUrl, int expirationMinutes = 60)
        {
            var t = EmailText.For(culture, Set);
            var minutes = expirationMinutes.ToString(CultureInfo.InvariantCulture);

            return $@"{brand.Name} - {t["Heading"]}

{t.Format("HelloFullName", firstName, lastName)}

{t.Format("Intro", brand.Name)}

{t["VisitLink"]}
{resetUrl}

{t["ImportantUpper"]} {t.Format("Expiry", minutes)}

{t["Questions"]}

{t["BestRegards"]}
{t.Format("TheBrandTeam", brand.Name)}

{t["AutomatedMessage"]}
{Copyright(t, brand)}";
        }
    }
}
