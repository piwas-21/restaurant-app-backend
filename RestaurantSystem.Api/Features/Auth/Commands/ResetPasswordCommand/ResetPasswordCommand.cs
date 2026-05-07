using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand;

public record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword) : ICommand<ApiResponse<string>>;

public class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            _logger.LogWarning("Password reset attempted for non-existent email: {Email}", command.Email);
            return ApiResponse<string>.Failure("Invalid reset request", "Password reset failed");
        }


        var result = await _userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Password reset failed for user {UserId}: {Errors}", user.Id, string.Join(", ", errors));
            return ApiResponse<string>.Failure(errors, "Password reset failed");
        }

        // Update audit fields
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = "PasswordReset";
        await _userManager.UpdateAsync(user);

        // Send password changed notification email
        try
        {
            await _emailService.SendPasswordChangedNotificationAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password changed notification to user {UserId}", user.Id);
            // Don't fail the operation if email notification fails
        }

        _logger.LogInformation("Password successfully reset for user {UserId}", user.Id);

        return ApiResponse<string>.SuccessWithData(
            "Password has been reset successfully",
            "Password reset completed");
    }
}
