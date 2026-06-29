namespace RestaurantSystem.Api.Common.Models;

/// <summary>
/// A file attached to an outgoing email. When <see cref="ContentId"/> is set the
/// attachment is referenced inline from the HTML body via <c>cid:&lt;ContentId&gt;</c>
/// (e.g. an embedded QR image); otherwise it is sent as a regular attachment.
/// </summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType, string? ContentId = null);

/// <summary>
/// Transport-agnostic representation of one email, built by <c>EmailService</c> and
/// handed to an <c>IEmailSender</c> implementation (SMTP or Resend).
/// </summary>
public sealed record OutgoingEmail(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);
