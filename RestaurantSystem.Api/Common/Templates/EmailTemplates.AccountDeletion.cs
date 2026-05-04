namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Account deletion email template
    /// </summary>
    public static class AccountDeletion
    {
        public static string Subject => "Action Required: Account Deletion Request - Restaurant System";

        public static string GetHtmlBody(string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Account Deletion Request</title>
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
            <h1>Account Deletion Request</h1>
        </div>
        <div class='content'>
            <h2>Hello {firstName} {lastName},</h2>
            <p>We received a request to delete your Restaurant System account. Your account is scheduled for permanent deletion on <strong>{scheduledDeletionDate:MMMM dd, yyyy}</strong>.</p>

            <p>You have two options:</p>

            <div style='text-align: center;'>
                <p><strong>Option 1: Delete Immediately</strong></p>
                <p>Click the button below to permanently delete your account right now. This action cannot be undone.</p>
                <a href='{deleteUrl}' class='button-delete'>Delete Account Immediately</a>
            </div>

            <div style='text-align: center; margin-top: 30px;'>
                <p><strong>Option 2: Cancel Deletion</strong></p>
                <p>If you changed your mind, simply log in to your account. This will automatically cancel the deletion request.</p>
                <a href='{cancelUrl}' class='button-cancel'>Login to Cancel</a>
            </div>

            <div class='warning'>
                <strong>⚠️ Important:</strong> If you take no action, your account will be automatically deleted on the date shown above.
            </div>

            <p>If you did not request this, please change your password immediately.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message, please do not reply to this email.</p>
            <p>© 2024 Restaurant System. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(string firstName, string lastName, string deleteUrl, string cancelUrl, DateTime scheduledDeletionDate)
        {
            return $@"Restaurant System - Account Deletion Request

Hello {firstName} {lastName},

We received a request to delete your Restaurant System account. Your account is scheduled for permanent deletion on {scheduledDeletionDate:MMMM dd, yyyy}.

You have two options:

Option 1: Delete Immediately
Visit the following link to permanently delete your account right now. This action cannot be undone.
{deleteUrl}

Option 2: Cancel Deletion
If you changed your mind, simply log in to your account. This will automatically cancel the deletion request.
{cancelUrl}

IMPORTANT: If you take no action, your account will be automatically deleted on the date shown above.

If you did not request this, please change your password immediately.

This is an automated message, please do not reply to this email.
© 2024 Restaurant System. All rights reserved.";
        }
    }
}
