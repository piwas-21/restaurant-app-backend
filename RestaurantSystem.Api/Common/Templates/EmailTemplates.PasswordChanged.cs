namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Password changed notification template
    /// </summary>
    public static class PasswordChanged
    {
        public static string Subject => "Password Changed - Restaurant System";

        public static string GetHtmlBody(string firstName, string lastName, DateTime changedAt)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Password Changed</title>
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
            <h1>🔐 Restaurant System</h1>
        </div>
        <div class='content'>
            <h2>Password Changed Successfully</h2>
            <p>Hello {firstName} {lastName},</p>
            <div class='alert'>
                <strong>✅ Your password has been successfully changed.</strong><br>
                Changed on: {changedAt:F}
            </div>
            <p>If you made this change, no further action is required.</p>
            <p><strong>If you didn't change your password:</strong></p>
            <ul>
                <li>Someone else may have access to your account</li>
                <li>Contact our support team immediately</li>
                <li>Consider changing your password again</li>
            </ul>
            <p>For your security, always use a strong, unique password and never share it with others.</p>
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

        public static string GetTextBody(string firstName, string lastName, DateTime changedAt)
        {
            return $@"Restaurant System - Password Changed

Hello {firstName} {lastName},

Your password has been successfully changed on {changedAt:F}.

If you made this change, no further action is required.

If you didn't change your password:
- Someone else may have access to your account
- Contact our support team immediately
- Consider changing your password again

For your security, always use a strong, unique password and never share it with others.

Best regards,
The Restaurant System Team

This is an automated message, please do not reply to this email.
© {DateTime.Now.Year} Restaurant System. All rights reserved.";
        }
    }
}
