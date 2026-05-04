namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Welcome email template
    /// </summary>
    public static class Welcome
    {
        public static string Subject => "Welcome to Restaurant System! 🍽️";

        public static string GetHtmlBody(string firstName, string lastName, string role)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Welcome</title>
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
            <h1>🍽️ Welcome to Restaurant System!</h1>
        </div>
        <div class='content'>
            <h2>Welcome aboard, {firstName}!</h2>
            <p>Congratulations! Your account has been successfully created with the role of <strong>{role}</strong>.</p>

            <div class='feature'>
                <h3>🔐 Your Account Security</h3>
                <p>Your account is protected with industry-standard security measures. Always keep your password safe and never share it with others.</p>
            </div>

            <div class='feature'>
                <h3>🚀 Getting Started</h3>
                <p>You can now log in to your account and start using all the features available to you based on your role.</p>
            </div>

            <div class='feature'>
                <h3>💡 Need Help?</h3>
                <p>If you have any questions or need assistance, our support team is here to help. Contact us anytime!</p>
            </div>

            <p>Thank you for joining Restaurant System. We're excited to have you on board!</p>
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

        public static string GetTextBody(string firstName, string lastName, string role)
        {
            return $@"Welcome to Restaurant System!

Welcome aboard, {firstName}!

Congratulations! Your account has been successfully created with the role of {role}.

Your Account Security:
Your account is protected with industry-standard security measures. Always keep your password safe and never share it with others.

Getting Started:
You can now log in to your account and start using all the features available to you based on your role.

Need Help?
If you have any questions or need assistance, our support team is here to help. Contact us anytime!

Thank you for joining Restaurant System. We're excited to have you on board!

Best regards,
The Restaurant System Team

This is an automated message, please do not reply to this email.
© 2024 Restaurant System. All rights reserved.";
        }
    }
}
