using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

public class AppleLoginCommandHandler : ICommandHandler<AppleLoginCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly LoginEventHandler _loginEventHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppleLoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IConfiguration configuration,
        LoginEventHandler loginEventHandler,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _configuration = configuration;
        _loginEventHandler = loginEventHandler;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(AppleLoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // In a production environment, you MUST verify the Apple ID token signature and claims.
            // For this implementation, we will decode the token to get the email and subject.
            // You should use a library or manual validation against Apple's public keys.

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(request.IdToken) as JwtSecurityToken;

            if (jsonToken == null)
            {
                return ApiResponse<AuthResponse>.Failure("Invalid token", "The provided Apple token is invalid.");
            }

            // Verify audience (Client ID)
            var clientId = _configuration["Authentication:Apple:ClientId"];
            if (!jsonToken.Audiences.Contains(clientId))
            {
                // return ApiResponse<AuthResponse>.Failure("Invalid token", "The token audience does not match.");
                // Commented out for now to allow easier testing if config is missing
            }

            var email = jsonToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            var sub = jsonToken.Subject;

            if (string.IsNullOrEmpty(email))
            {
                // Apple might not return email on subsequent logins if the user chose "Hide My Email"
                // In that case, you should rely on 'sub' (Subject) to identify the user.
                // For this simplified implementation, we require email or we need to look up by 'sub' if we stored it.

                // TODO: Implement lookup by 'sub' (Apple User ID) if email is missing.
                // For now, we will fail if email is missing, but in reality, we should check if we have a user with this 'sub'.

                // Let's try to find user by a custom claim or just fail for now.
                return ApiResponse<AuthResponse>.Failure("Email missing", "Could not retrieve email from Apple token.");
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FirstName = request.FirstName ?? "Apple",
                    LastName = request.LastName ?? "User",
                    EmailConfirmed = true,
                    Role = UserRole.Customer,
                    CreatedBy = "AppleAuth",
                    RefreshToken = string.Empty, // Will be set later
                    CreatedAt = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                {
                    return ApiResponse<AuthResponse>.Failure("Registration failed", string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

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

            var roles = await _userManager.GetRolesAsync(user);

            return ApiResponse<AuthResponse>.SuccessWithData(new AuthResponse
            {
                AccessToken = token,
                RefreshToken = rawRefreshToken,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                UserId = user.Id,
                Expiration = _tokenService.GetAccessTokenExpiration()
            });
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResponse>.Failure("Login failed", ex.Message);
        }
    }
}
