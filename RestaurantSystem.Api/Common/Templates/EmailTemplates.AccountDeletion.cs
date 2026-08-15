using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Account deletion email template
    /// </summary>
    public static class AccountDeletion
    {
        private const string Set = "AccountDeletion";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
        {
            var t = EmailText.For(culture, Set);
            var scheduled = LongDate(scheduledDeletionDate, culture);

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
        .header {{ background: #c0392b; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .button-delete {{ display: inline-block; padding: 12px 30px; background: #c0392b; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .button-cancel {{ display: inline-block; padding: 12px 30px; background: #7f8c8d; color: white; text-decoration: none; border-radius: 5px; margin: 20px 10px; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .warning {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{t["PageTitle"]}</h1>
        </div>
        <div class='content'>
            <h2>{t.Format("HelloFullName", EmailHtml.Encode(firstName), EmailHtml.Encode(lastName))}</h2>
            <p>{t.Format("Scheduled", brand.Name, $"<strong>{scheduled}</strong>")}</p>

            <p>{t["TwoOptions"]}</p>

            <div style='text-align: center;'>
                <p><strong>{t["Option1Title"]}</strong></p>
                <p>{t["Option1BodyHtml"]}</p>
                <a href='{deleteUrl}' class='button-delete'>{t["Option1Button"]}</a>
            </div>

            <div style='text-align: center; margin-top: 30px;'>
                <p><strong>{t["Option2Title"]}</strong></p>
                <p>{t["Option2Body"]}</p>
                <a href='{cancelUrl}' class='button-cancel'>{t["Option2Button"]}</a>
            </div>

            <div class='warning'>
                <strong>⚠️ {t["ImportantLabel"]}</strong> {t["NoActionWarning"]}
            </div>

            <p>{t["NotYou"]}</p>
        </div>
        <div class='footer'>
            <p>{t["AutomatedMessage"]}</p>
            <p>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
        {
            var t = EmailText.For(culture, Set);
            var scheduled = LongDate(scheduledDeletionDate, culture);

            return $@"{brand.Name} - {t["PageTitle"]}

{t.Format("HelloFullName", firstName, lastName)}

{t.Format("Scheduled", brand.Name, scheduled)}

{t["TwoOptions"]}

{t["Option1Title"]}
{t["Option1BodyText"]}
{deleteUrl}

{t["Option2Title"]}
{t["Option2Body"]}
{cancelUrl}

{t["ImportantUpper"]} {t["NoActionWarning"]}

{t["NotYou"]}

{t["AutomatedMessage"]}
{Copyright(t, brand)}";
        }
    }
}
