using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.ChangePasswordCommand;

public record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
) : ICommand<ApiResponse<string>>;

public class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;

        if (!userId.HasValue)
        {
            _logger.LogWarning("Change password attempted without authenticated user");
            return ApiResponse<string>.Failure("User not authenticated");
        }

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());

        if (user == null)
        {
            _logger.LogWarning("User not found for ID: {UserId}", userId);
            return ApiResponse<string>.Failure("User not found");
        }

        // Verify current password
        var isCurrentPasswordValid = await _userManager.CheckPasswordAsync(user, command.CurrentPassword);

        if (!isCurrentPasswordValid)
        {
            _logger.LogWarning("Invalid current password for user: {Email}", user.Email);
            return ApiResponse<string>.Failure("Current password is incorrect");
        }

        // Change password
        var result = await _userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to change password for user {Email}: {Errors}", user.Email, errors);
            return ApiResponse<string>.Failure($"Failed to change password: {errors}");
        }

        // Invalidate existing refresh tokens so active sessions must re-authenticate
        user.RefreshToken = string.Empty;
        user.RefreshTokenExpiryTime = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Password changed successfully for user: {Email}", user.Email);
        return ApiResponse<string>.SuccessWithData("Password changed successfully");
    }
}
