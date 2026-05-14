using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

public class GoogleLoginCommandHandler : ICommandHandler<GoogleLoginCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly LoginEventHandler _loginEventHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GoogleLoginCommandHandler(
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

    public async Task<ApiResponse<AuthResponse>> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings()
            {
                Audience = new List<string>()
                {
                    _configuration["Authentication:Google:ClientId"]!,          // Web
                    _configuration["Authentication:Google:AndroidClientId"]!,   // Android
                    _configuration["Authentication:Google:IosClientId"]!        // iOS
                }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);

            var user = await _userManager.FindByEmailAsync(payload.Email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    FirstName = payload.GivenName,
                    LastName = payload.FamilyName,
                    EmailConfirmed = true, // Google emails are verified
                    Role = UserRole.Customer,
                    CreatedBy = "GoogleAuth",
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
        catch (InvalidJwtException)
        {
            return ApiResponse<AuthResponse>.Failure("Invalid token", "The provided Google token is invalid.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResponse>.Failure("Login failed", ex.Message);
        }
    }
}
