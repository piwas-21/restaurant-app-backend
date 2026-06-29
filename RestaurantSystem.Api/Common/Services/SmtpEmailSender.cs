using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// SMTP email transport (System.Net.Mail). Works where outbound SMTP is reachable
/// (e.g. local development). Selected when <c>EmailSettings:Provider</c> is "Smtp" (default).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public SmtpEmailSender(IOptions<EmailSettings> settings) => _settings = settings.Value;

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.EnableSsl,
            Timeout = _settings.TimeoutMs
        };
        if (_settings.UseAuthentication)
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);

        // Build addresses first: their ctors throw FormatException on invalid input,
        // before any IDisposable is allocated.
        var from = new MailAddress(_settings.FromEmail, _settings.FromName);
        var to = new MailAddress(email.To);

        var attachments = email.Attachments ?? [];
        var inline = attachments.Where(a => a.ContentId is not null).ToList();
        var files = attachments.Where(a => a.ContentId is null).ToList();

        var streams = new List<MemoryStream>();
        try
        {
            using var message = new MailMessage
            {
                From = from,
                Subject = email.Subject,
                SubjectEncoding = Encoding.UTF8,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8
            };
            message.To.Add(to);

            if (!string.IsNullOrEmpty(email.TextBody))
                message.AlternateViews.Add(
                    AlternateView.CreateAlternateViewFromString(email.TextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));

            var htmlView = AlternateView.CreateAlternateViewFromString(email.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
            foreach (var att in inline)
            {
                var stream = new MemoryStream(att.Content);
                streams.Add(stream);
                htmlView.LinkedResources.Add(new LinkedResource(stream, att.ContentType) { ContentId = att.ContentId });
            }
            message.AlternateViews.Add(htmlView);

            foreach (var att in files)
            {
                var stream = new MemoryStream(att.Content);
                streams.Add(stream);
                message.Attachments.Add(new Attachment(stream, att.FileName, att.ContentType));
            }

            await client.SendMailAsync(message, cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }
    }
}
