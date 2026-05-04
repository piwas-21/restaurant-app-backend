namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Password reset email template
    /// </summary>
    public static class PasswordReset
    {
        public static string Subject => "Reset Your Password - Restaurant System";

        public static string GetHtmlBody(string firstName, string lastName, string resetUrl, int expirationMinutes = 60)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Password Reset</title>
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
            <h1>🍽️ Restaurant System</h1>
        </div>
        <div class='content'>
            <h2>Password Reset Request</h2>
            <p>Hello {firstName} {lastName},</p>
            <p>We received a request to reset your password for your Restaurant System account. If you didn't make this request, please ignore this email.</p>
            <p>To reset your password, click the button below:</p>
            <div style='text-align: center;'>
                <a href='{resetUrl}' class='button'>Reset Password</a>
            </div>
            <p>Or copy and paste this link into your browser:</p>
            <p style='word-break: break-all;'>{resetUrl}</p>
            <div class='warning'>
                <strong>⚠️ Important:</strong> This link will expire in {expirationMinutes} minutes for security reasons.
            </div>
            <p>If you have any questions, please contact our support team.</p>
            <p>Best regards,<br>The Restaurant System Team</p>
        </div>
        <div class='footer'>
            <p>This is an automated message, please do not reply to this email.</p>
            <p>© 2024 Restaurant System. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(string firstName, string lastName, string resetUrl, int expirationMinutes = 60)
        {
            return $@"Restaurant System - Password Reset Request

Hello {firstName} {lastName},

We received a request to reset your password for your Restaurant System account. If you didn't make this request, please ignore this email.

To reset your password, visit the following link:
{resetUrl}

IMPORTANT: This link will expire in {expirationMinutes} minutes for security reasons.

If you have any questions, please contact our support team.

Best regards,
The Restaurant System Team

This is an automated message, please do not reply to this email.
© 2024 Restaurant System. All rights reserved.";
        }
    }
}
