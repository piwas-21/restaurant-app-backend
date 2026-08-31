using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Entities;
using System.Security.Claims;

namespace RestaurantSystem.Api.Features.Auth.Commands.RefreshTokenCommand;

public record RefreshTokenCommand(string AccessToken, string RefreshToken) : ICommand<ApiResponse<AuthResponse>>;

/// <summary>Validates and rotates exactly one refresh session.</summary>
public class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshSessionService _sessions;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IRefreshSessionService sessions,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(command.AccessToken);
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || user.IsDeleted)
            {
                _logger.LogWarning("Token refresh attempted for unavailable user {UserId}", userId);
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            var newRefreshToken = await _sessions.RotateAsync(user, command.RefreshToken, cancellationToken);
            if (newRefreshToken is null)
            {
                _logger.LogWarning("Invalid or expired refresh token attempt for user {UserId}", user.Id);
                return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
            }

            return ApiResponse<AuthResponse>.SuccessWithData(new AuthResponse
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email!,
                Role = user.Role,
                AccessToken = _tokenService.GenerateAccessToken(user),
                RefreshToken = newRefreshToken,
                Expiration = _tokenService.GetAccessTokenExpiration()
            }, "Token refreshed successfully");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Security token exception during refresh");
            return ApiResponse<AuthResponse>.Failure("Invalid token", "Token refresh failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while refreshing token");
            return ApiResponse<AuthResponse>.Failure("Token refresh failed", "An unexpected error occurred");
        }
    }
}
