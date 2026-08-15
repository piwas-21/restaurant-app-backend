using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.SendEmailVerificationCommand;

public record SendEmailVerificationCommand(string Email) : ICommand<ApiResponse<string>>;

/// <summary>
/// Resends the email-verification mail. <b>Every</b> branch answers the same generic sentence so
/// the endpoint cannot be used to probe which addresses exist — unknown address, cooled-down
/// address, already-verified address, a lost concurrency race and a send that threw are all
/// indistinguishable. The already-verified branch used to answer "Email is already verified.",
/// which made this endpoint an oracle for exactly the accounts worth finding; nothing reads that
/// string (both callers only check <c>success</c>), so it is gone.
///
/// <para>
/// Abuse control is two-layer (GAP-3): the "email-verification" per-IP policy on the controller
/// caps one caller, and the per-address cooldown here caps how often a single inbox can be mailed
/// however many IPs ask — which is the bombing attack the IP limit alone cannot see.
/// </para>
/// </summary>
public class SendEmailVerificationCommandHandler : ICommandHandler<SendEmailVerificationCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly RateLimiterSettings _rateLimiter;
    private readonly ILogger<SendEmailVerificationCommandHandler> _logger;

    public SendEmailVerificationCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        IOptions<RateLimiterSettings> rateLimiter,
        ILogger<SendEmailVerificationCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _languages = languages;
        _rateLimiter = rateLimiter.Value;
        _logger = logger;
    }

    // The one sentence every branch returns. Anti-enumeration: unknown address, cooled-down
    // address and a send that threw are indistinguishable to the caller.
    private static ApiResponse<string> Acknowledged() =>
        ApiResponse<string>.SuccessWithData(
            "If the email exists in our system, a verification link has been sent.",
            "Email verification request processed");

    public async Task<ApiResponse<string>> Handle(SendEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            // No address in the log line: this endpoint is anonymous and now attacker-drivable at
            // volume, so logging what was asked for would be both a PII sink and a spam amplifier.
            _logger.LogDebug("Email verification requested for an address that does not exist");
            return Acknowledged();
        }

        if (user.EmailConfirmed)
        {
            _logger.LogDebug("Email verification requested for user {UserId}, already verified", user.Id);
            return Acknowledged();
        }

        if (IsWithinCooldown(user))
        {
            _logger.LogInformation(
                "Email verification resend for user {UserId} suppressed by the {Minutes}-minute per-address cooldown",
                user.Id, _rateLimiter.EmailVerificationCooldownMinutes);
            return Acknowledged();
        }

        // Stamp BEFORE sending, and keep the stamp even if the send throws. The cooldown counts
        // attempts, not deliveries: releasing it on failure would hand the bombing vector back at
        // exactly the moment the mail provider is unhealthy (each attempt still costs a provider
        // call and up to 3 retries). A genuine user waits out one cooldown and asks again.
        //
        // This is the opposite of IOutboundEmailLedger's release-on-failure (GAP-11), and
        // deliberately so: a ledger claim is forever, so keeping it would make that mail
        // permanently unsendable, while a cooldown is time-bounded and always expires. The ledger
        // answers "was this ever sent?", which is the wrong question for a mail whose whole point
        // is that it can be legitimately re-requested.
        user.LastEmailVerificationSentAt = DateTime.UtcNow;
        var stamped = await _userManager.UpdateAsync(user);
        if (!stamped.Succeeded)
        {
            // Two very different failures share this return. A concurrency failure is benign — a
            // parallel request is already sending this very mail. Anything else (a legacy row the
            // UserValidator now rejects) is permanent: that user will never receive the mail, and
            // logging it as "concurrent" would send whoever debugs it looking for a race that
            // isn't there. The response is identical either way — a caller must not learn which.
            if (stamped.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            {
                _logger.LogInformation(
                    "Concurrent email-verification request for user {UserId}; this one sends nothing", user.Id);
            }
            else
            {
                _logger.LogError(
                    "Could not stamp the verification cooldown for user {UserId}, so no mail was sent: {Errors}",
                    user.Id, string.Join(", ", stamped.Errors.Select(e => e.Code)));
            }

            return Acknowledged();
        }

        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            // The account's own preference. This endpoint is anonymous and attacker-drivable
            // (see the cooldown above), so the caller's Accept-Language is not the recipient's.
            await _emailService.SendEmailVerificationAsync(_languages.ForAccount(user), user, token);

            _logger.LogInformation("Email verification sent successfully for user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email verification for user {UserId}", user.Id);
            // Don't reveal the error to the user for security reasons
        }

        return Acknowledged();
    }

    private bool IsWithinCooldown(ApplicationUser user)
    {
        var minutes = _rateLimiter.EmailVerificationCooldownMinutes;
        if (minutes <= 0 || user.LastEmailVerificationSentAt is not { } lastSentAt)
            return false;

        return DateTime.UtcNow - lastSentAt < TimeSpan.FromMinutes(minutes);
    }
}
