using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Group membership confirmation email, with the member's QR code embedded as an
    /// inline attachment (<c>cid:qrcode</c>).
    /// <para>
    /// Extracted from <c>EmailService</c>, where it was the one mail built inline
    /// (EMAIL-SPEC-TENANT-APP GAP-23). The two "Add to Wallet" buttons that came with it
    /// were dead — both linked to <c>href="#"</c> under a "coming soon" caption — and are
    /// gone rather than translated fifteen times.
    /// </para>
    /// </summary>
    public static class MembershipConfirmation
    {
        private const string Set = "MembershipConfirmation";

        public static string GetSubject(CultureInfo culture, string groupName) =>
            EmailText.For(culture, Set).Format("Subject", groupName);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string userName, string groupName,
            string groupDescription, DateTime? expiryDate = null)
        {
            var t = EmailText.For(culture, Set);
            var expiryText = expiryDate.HasValue
                ? $"<p><strong>{t["ExpiresLabel"]}</strong> {LongDate(expiryDate.Value, culture)}</p>"
                : "";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4a90e2; color: white; padding: 20px; text-align: center; border-radius: 8px 8px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 30px; border-radius: 0 0 8px 8px; }}
        .qr-container {{ text-align: center; margin: 30px 0; padding: 20px; background: white; border-radius: 8px; }}
        .qr-code {{ max-width: 300px; height: auto; }}
        .footer {{ text-align: center; margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>{t.Format("Heading", EmailHtml.Encode(groupName))}</h1>
        </div>
        <div class=""content"">
            <p>{Greeting(t, "Hello", userName, encode: true)}</p>
            <p>{t.Format("Added", $"<strong>{EmailHtml.Encode(groupName)}</strong>")}</p>
            <p>{EmailHtml.Encode(groupDescription)}</p>
            {expiryText}

            <div class=""qr-container"">
                <h3>{t["QrTitle"]}</h3>
                <img src=""cid:qrcode"" alt=""{t["QrAlt"]}"" class=""qr-code"" />
                <p style=""font-size: 12px; color: #666; margin-top: 10px;"">{t["QrHint"]}</p>
            </div>
        </div>
        <div class=""footer"">
            <p>{brand.Name} | {t["ValuedMember"]}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string userName, string groupName,
            string groupDescription, string qrCodeData, DateTime? expiryDate = null)
        {
            var t = EmailText.For(culture, Set);
            var expiryLine = expiryDate.HasValue
                ? t.Format("ExpiresLine", LongDate(expiryDate.Value, culture))
                : "";

            return $@"{t.Format("Heading", groupName)}

{Greeting(t, "Hello", userName)}

{t.Format("Added", groupName)}

{groupDescription}

{expiryLine}

{t["QrAttached"]}

{t["QrDataLabel"]} {qrCodeData}

---
{brand.Name}
{t["ValuedMember"]}";
        }
    }
}
