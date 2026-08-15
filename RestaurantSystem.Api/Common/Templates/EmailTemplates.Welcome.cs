using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Welcome email template
    /// </summary>
    public static class Welcome
    {
        private const string Set = "Welcome";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string role)
        {
            var t = EmailText.For(culture, Set);
            var greeting = t.Format("Greeting", EmailHtml.Encode(firstName));
            var accountCreated = t.Format("AccountCreated", $"<strong>{EmailHtml.Encode(RoleLabel(t, role))}</strong>");
            var thankYou = t.Format("ThankYou", brand.Name);

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
        .header {{ background: #27ae60; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .feature {{ background: white; padding: 15px; margin: 10px 0; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {t.Format("Header", brand.Name)}</h1>
        </div>
        <div class='content'>
            <h2>{greeting}</h2>
            <p>{accountCreated}</p>

            <div class='feature'>
                <h3>🔐 {t["SecurityTitle"]}</h3>
                <p>{t["SecurityBody"]}</p>
            </div>

            <div class='feature'>
                <h3>🚀 {t["GettingStartedTitle"]}</h3>
                <p>{t["GettingStartedBody"]}</p>
            </div>

            <div class='feature'>
                <h3>💡 {t["HelpTitle"]}</h3>
                <p>{t["HelpBody"]}</p>
            </div>

            <p>{thankYou}</p>
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

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string role)
        {
            var t = EmailText.For(culture, Set);

            return $@"{t.Format("Header", brand.Name)}

{t.Format("Greeting", firstName)}

{t.Format("AccountCreated", RoleLabel(t, role))}

{Heading(t, t["SecurityTitle"])}
{t["SecurityBody"]}

{Heading(t, t["GettingStartedTitle"])}
{t["GettingStartedBody"]}

{t["HelpTitle"]}
{t["HelpBody"]}

{t.Format("ThankYou", brand.Name)}

{t["BestRegards"]}
{t.Format("TheBrandTeam", brand.Name)}

{t["AutomatedMessage"]}
{Copyright(t, brand)}";
        }
    }
}
