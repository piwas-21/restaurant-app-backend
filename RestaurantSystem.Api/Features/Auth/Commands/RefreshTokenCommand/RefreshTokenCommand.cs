using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Entities;
using System.Security.Claims;

namespace RestaurantSystem.Api.Features.Auth.Commands.RefreshTokenCommand;

public record RefreshTokenCommand(
    string AccessToken,      // The expired access token
    string RefreshToken      // The long-lived refresh token
) : ICommand<ApiResponse<AuthResponse>>;

/// <summary>
/// Handler for RefreshTokenCommand - Validates refresh token and generates new access token
/// </summary>
public class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Extract user information from expired access token
            var principal = _tokenService.GetPrincipalFromExpiredToken(command.AccessToken);
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Invalid access token - no user ID found in claims");
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            // Step 2: Find user in database
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null || user.IsDeleted)
            {
                _logger.LogWarning("Token refresh attempted for non-existent or deleted user {UserId}", userId);
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            // Step 3: Validate refresh token
            if (user.RefreshToken != command.RefreshToken)
            {
                _logger.LogWarning("Invalid refresh token attempt for user {UserId} - token mismatch", user.Id);
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            if (user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                _logger.LogWarning("Expired refresh token attempt for user {UserId}", user.Id);
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            // Step 4: Generate new tokens (token rotation for security)
            var newAccessToken = _tokenService.GenerateAccessToken(user);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            // Step 5: Update user with new refresh token
            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7); // Configurable refresh token expiry
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = user.Id.ToString();

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                var errors = updateResult.Errors.Select(e => e.Description).ToList();
                _logger.LogError("Failed to update user {UserId} during token refresh: {Errors}",
                    user.Id, string.Join(", ", errors));
                return ApiResponse<AuthResponse>.Failure("Token refresh failed", "Internal error occurred");
            }

            _logger.LogInformation("Token refreshed successfully for user {UserId}", user.Id);

            // Step 6: Return new authentication response
            var authResponse = new AuthResponse
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email!,
                Role = user.Role,
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                Expiration = _tokenService.GetAccessTokenExpiration()
            };

            return ApiResponse<AuthResponse>.SuccessWithData(authResponse, "Token refreshed successfully");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Security token exception during refresh for token: {Token}",
                command.AccessToken?.Substring(0, Math.Min(20, command.AccessToken.Length)) + "...");
            return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while refreshing token");
            return ApiResponse<AuthResponse>.Failure("Token refresh failed", "An unexpected error occurred");
        }
    }
}
