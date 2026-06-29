using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Low-level email transport. The concrete implementation is selected by
/// <c>EmailSettings:Provider</c> ("Smtp" or "Resend"). <c>EmailService</c> owns the
/// templates, enable/log-only checks and retry policy; the sender only delivers one message.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}
