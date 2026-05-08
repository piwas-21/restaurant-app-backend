using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

public record LoginCommand(string Email, string Password) : ICommand<ApiResponse<AuthResponse>>;

public class LoginCommandHandler : ICommandHandler<LoginCommand, ApiResponse<AuthResponse>>
{

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly LoginEventHandler _loginEventHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        LoginEventHandler loginEventHandler,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _loginEventHandler = loginEventHandler;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {

        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        // Verify password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, command.Password);

        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        // Check if email is confirmed (only for customers)
        if (!user.EmailConfirmed && user.Role == Domain.Common.Enums.UserRole.Customer)
        {
            return ApiResponse<AuthResponse>.Failure(
                "Please verify your email address before logging in. Check your inbox for the verification link.",
                "Email verification required");
        }

        // Generate tokens — store hash of refresh token, return raw value to client
        var token = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        user.RefreshToken = _tokenService.HashRefreshToken(rawRefreshToken);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(user);

        // Merge anonymous basket if session ID exists
        var sessionId = _httpContextAccessor.HttpContext?.Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _loginEventHandler.HandleUserLogin(user.Id, sessionId);
        }

        var authResponse = new AuthResponse
        {
            UserId = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email!,
            Role = user.Role,
            AccessToken = token,
            RefreshToken = rawRefreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        var message = "User logged in successfully";
        if (user.DeletionScheduledAt.HasValue)
        {
            user.DeletionScheduledAt = null;
            await _userManager.UpdateAsync(user);
            message = "User logged in successfully. Account deletion request cancelled.";
        }

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, message);

    }
}
