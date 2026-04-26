using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.VerifyEmailCommand;

public record VerifyEmailCommand(string Email, string Token) : ICommand<ApiResponse<string>>;

public class VerifyEmailCommandHandler : ICommandHandler<VerifyEmailCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Email verification attempt for: {Email}", command.Email);

        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            _logger.LogWarning("Email verification attempted for non-existent or deleted email: {Email}", command.Email);
            return ApiResponse<string>.Failure("Invalid verification request", "Email verification failed");
        }

        _logger.LogInformation("User found: {UserId}, EmailConfirmed: {EmailConfirmed}", user.Id, user.EmailConfirmed);

        if (user.EmailConfirmed)
        {
            _logger.LogInformation("Email already verified for user {UserId}", user.Id);
            return ApiResponse<string>.SuccessWithData(
                "Email is already verified.",
                "Email verification completed");
        }

        _logger.LogInformation("Attempting to confirm email for user {UserId} with token", user.Id);
        var result = await _userManager.ConfirmEmailAsync(user, command.Token);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Email verification failed for user {UserId}: {Errors}", user.Id, string.Join(", ", errors));
            return ApiResponse<string>.Failure(errors, "Email verification failed. The link may have expired or is invalid.");
        }

        // Update audit fields
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = "EmailVerification";
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Email successfully verified for user {UserId}. EmailConfirmed is now: {EmailConfirmed}", user.Id, user.EmailConfirmed);

        return ApiResponse<string>.SuccessWithData(
            "Your email has been verified successfully! You can now log in.",
            "Email verification completed");
    }
}
