namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Email verification template
    /// </summary>
    public static class EmailVerification
    {
        public static string Subject => "Verify Your Email - RUMI Restaurant";

        public static string GetHtmlBody(string firstName, string lastName, string verificationUrl)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta http-equiv='X-UA-Compatible' content='IE=edge'>
    <title>Verify Your Email - RUMI Restaurant</title>
    <!--[if mso]>
    <style type='text/css'>
        body, table, td {{font-family: Arial, Helvetica, sans-serif !important;}}
    </style>
    <![endif]-->
</head>
<body style='margin: 0; padding: 0; background-color: #f5f5f5; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;'>
    <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='background-color: #f5f5f5;'>
        <tr>
            <td style='padding: 40px 20px;'>
                <!-- Main Container -->
                <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08);'>

                    <!-- Header with Gradient -->
                    <tr>
                        <td style='background: linear-gradient(135deg, #c79063 0%, #a67c52 100%); padding: 40px 30px; text-align: center;'>
                            <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                                <tr>
                                    <td style='text-align: center;'>
                                        <!-- Logo/Brand -->
                                        <div style='background-color: rgba(255, 255, 255, 0.15); backdrop-filter: blur(10px); border-radius: 50%; width: 80px; height: 80px; margin: 0 auto 20px; display: flex; align-items: center; justify-content: center; border: 3px solid rgba(255, 255, 255, 0.3);'>
                                            <span style='font-size: 40px; color: #ffffff;'>🍽️</span>
                                        </div>
                                        <h1 style='margin: 0; color: #ffffff; font-size: 32px; font-weight: 800; letter-spacing: 2px; text-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);'>RUMI</h1>
                                        <p style='margin: 8px 0 0; color: rgba(255, 255, 255, 0.95); font-size: 14px; font-weight: 500; letter-spacing: 1px;'>RESTAURANT</p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Content -->
                    <tr>
                        <td style='padding: 50px 40px;'>
                            <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                                <tr>
                                    <td>
                                        <h2 style='margin: 0 0 20px; color: #1f2937; font-size: 26px; font-weight: 700; line-height: 1.3;'>Welcome to RUMI! 🎉</h2>
                                        <p style='margin: 0 0 16px; color: #4b5563; font-size: 16px; line-height: 1.6;'>Hello <strong style='color: #c79063;'>{firstName} {lastName}</strong>,</p>
                                        <p style='margin: 0 0 24px; color: #4b5563; font-size: 16px; line-height: 1.6;'>Thank you for joining the RUMI family! We're excited to have you experience our culinary journey. To get started, please verify your email address.</p>

                                        <!-- Verification Button -->
                                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                                            <tr>
                                                <td style='text-align: center; padding: 30px 0;'>
                                                    <a href='{verificationUrl}' style='display: inline-block; padding: 16px 48px; background: linear-gradient(135deg, #c79063 0%, #a67c52 100%); color: #ffffff; text-decoration: none; border-radius: 50px; font-size: 16px; font-weight: 700; letter-spacing: 0.5px; box-shadow: 0 4px 15px rgba(199, 144, 99, 0.4); transition: all 0.3s ease;'>✓ Verify My Email</a>
                                                </td>
                                            </tr>
                                        </table>

                                        <!-- Divider -->
                                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='margin: 30px 0;'>
                                            <tr>
                                                <td style='border-top: 1px solid #e5e7eb; padding-top: 30px;'>
                                                    <p style='margin: 0 0 12px; color: #6b7280; font-size: 14px; line-height: 1.5;'><strong>Or copy and paste this link:</strong></p>
                                                    <div style='background-color: #f9fafb; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px; word-break: break-all;'>
                                                        <a href='{verificationUrl}' style='color: #c79063; text-decoration: none; font-size: 13px;'>{verificationUrl}</a>
                                                    </div>
                                                </td>
                                            </tr>
                                        </table>

                                        <!-- Info Box -->
                                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='margin-top: 30px;'>
                                            <tr>
                                                <td style='background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%); border-left: 4px solid #f59e0b; border-radius: 8px; padding: 20px;'>
                                                    <p style='margin: 0; color: #92400e; font-size: 14px; line-height: 1.6;'><strong>⏰ Quick Tip:</strong> This verification link will expire in 24 hours for security reasons. If you didn't create an account with RUMI, you can safely ignore this email.</p>
                                                </td>
                                            </tr>
                                        </table>

                                        <!-- Closing -->
                                        <p style='margin: 30px 0 0; color: #4b5563; font-size: 16px; line-height: 1.6;'>We can't wait to serve you!</p>
                                        <p style='margin: 8px 0 0; color: #4b5563; font-size: 16px; line-height: 1.6;'><strong style='color: #c79063;'>The RUMI Team</strong></p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background-color: #f9fafb; padding: 30px 40px; border-top: 1px solid #e5e7eb;'>
                            <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                                <tr>
                                    <td style='text-align: center;'>
                                        <p style='margin: 0 0 12px; color: #6b7280; font-size: 13px; line-height: 1.5;'>📍 Rue du Grand-Pré 45, 1202 Genève, Switzerland</p>
                                        <p style='margin: 0 0 20px; color: #9ca3af; font-size: 12px; line-height: 1.5;'>This is an automated message, please do not reply to this email.</p>
                                        <div style='border-top: 1px solid #e5e7eb; padding-top: 20px; margin-top: 20px;'>
                                            <p style='margin: 0; color: #9ca3af; font-size: 11px;'>© 2024 RUMI Restaurant. All rights reserved.</p>
                                        </div>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }

        public static string GetTextBody(string firstName, string lastName, string verificationUrl)
        {
            return $@"RUMI RESTAURANT - Email Verification
═══════════════════════════════════════

Welcome to RUMI! 🎉

Hello {firstName} {lastName},

Thank you for joining the RUMI family! We're excited to have you experience our culinary journey. To get started, please verify your email address by visiting the following link:

{verificationUrl}

⏰ Quick Tip: This verification link will expire in 24 hours for security reasons.

If you didn't create an account with RUMI, you can safely ignore this email.

We can't wait to serve you!

The RUMI Team

───────────────────────────────────────
📍 Rue du Grand-Pré 45, 1202 Genève, Switzerland

This is an automated message, please do not reply to this email.
© 2024 RUMI Restaurant. All rights reserved.";
        }
    }
}
