using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.SetPasswordCommand;

/// <summary>
/// Gives a PASSWORDLESS account its first password (mobile BACKEND-NOTES item 3).
///
/// Google/Apple sign-in creates an account with no password hash, so
/// <c>change-password</c> — which verifies a current password — can never succeed for it, and the
/// only way such a user could reach email+password sign-in was to run "forgot password" against
/// their own account. This is the in-app path.
///
/// Carries no user identifier: the account is resolved from the bearer token only.
/// </summary>
public record SetPasswordCommand(
    string NewPassword,
    string ConfirmPassword
) : ICommand<ApiResponse<string>>;

public class SetPasswordCommandHandler : ICommandHandler<SetPasswordCommand, ApiResponse<string>>
{
    /// <summary>
    /// Refused when the account already has a password. NOT a nicety: without it a stolen access
    /// token is enough to replace the password of a normal email+password account without knowing
    /// it, which is exactly what <c>change-password</c>'s current-password check prevents. The
    /// correct flow for an account that has one is <c>change-password</c> (or "forgot password").
    /// </summary>
    internal const string AlreadyHasPasswordMessage =
        "This account already has a password. Use change-password to change it.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly ILogger<SetPasswordCommandHandler> _logger;

    public SetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        ILogger<SetPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _emailService = emailService;
        _languages = languages;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(SetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetUserAsync();

        if (user is null)
        {
            // [Authorize] already refused a tokenless request; a valid token whose account is gone
            // is the same thing from the client's side — the session is over.
            throw new UnauthorizedAccessException("User not authenticated");
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            _logger.LogWarning("Set-password refused for user {UserId}: the account already has a password", user.Id);
            throw new BadRequestException(AlreadyHasPasswordMessage, ErrorCodes.PasswordAlreadySet);
        }

        // AddPasswordAsync, not ChangePasswordAsync: there is no current password to verify. It
        // runs Identity's own password validators (StrongPasswordValidator: repeats, common
        // passwords) on top of the FluentValidation policy, and rotates the security stamp.
        var result = await _userManager.AddPasswordAsync(user, command.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Set-password rejected for user {UserId}: {Errors}", user.Id, string.Join(", ", errors));
            throw new BadRequestException(string.Join("; ", errors)) { Errors = errors };
        }

        // Invalidate existing refresh tokens, exactly as change-password does: after a password
        // change every other session must re-authenticate.
        user.RefreshToken = string.Empty;
        user.RefreshTokenExpiryTime = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // The M4 "password changed" notification (EMAIL-SPEC-TENANT-APP §M4). Swallowed on failure
        // like ResetPasswordCommand does — a mail outage must not leave the caller believing the
        // password was not set when it was. NOTE: change-password still sends nothing at all
        // (GAP-8), which is tracked there, not here.
        try
        {
            await _emailService.SendPasswordChangedNotificationAsync(_languages.ForAccount(user), user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password changed notification to user {UserId}", user.Id);
        }

        _logger.LogInformation("Password set successfully for user {UserId}", user.Id);

        return ApiResponse<string>.SuccessWithData("Password set successfully");
    }
}
