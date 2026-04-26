using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.User.Commands.RegisterStaffCommand;

public record RegisterStaffCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string ConfirmPassword,
    UserRole Role) : ICommand<ApiResponse<AuthResponse>>;

public class RegisterStaffCommandHandler : ICommandHandler<RegisterStaffCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegisterStaffCommandHandler> _logger;

    public RegisterStaffCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        ILogger<RegisterStaffCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _currentUserService = currentUserService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RegisterStaffCommand command, CancellationToken cancellationToken)
    {
        // Check if current user is admin (this endpoint should be admin-only)
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || currentUser.Role != UserRole.Admin)
        {
            return ApiResponse<AuthResponse>.Failure("Unauthorized access", "Only administrators can register users with roles");
        }

        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(command.Email);
        if (existingUser != null)
        {
            return ApiResponse<AuthResponse>.Failure("User with this email already exists", "Registration failed");
        }

        // Create new user
        var newUser = new ApplicationUser
        {
            Email = command.Email,
            UserName = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Role = command.Role,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.Id.ToString(),
            RefreshToken = _tokenService.GenerateRefreshToken()
        };

        var result = await _userManager.CreateAsync(newUser, command.Password);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("User registration failed for email {Email}: {Errors}", command.Email, string.Join(", ", errors));
            return ApiResponse<AuthResponse>.Failure(errors, "Failed to create user");
        }

        // Generate tokens
        var token = _tokenService.GenerateAccessToken(newUser);
        newUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(newUser);

        // Send welcome email
        try
        {
            await _emailService.SendWelcomeEmailAsync(newUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to user {UserId}", newUser.Id);
            // Don't fail the registration if email sending fails
        }

        _logger.LogInformation("User {UserId} successfully registered by admin {AdminId} with role {Role}",
            newUser.Id, currentUser.Id, command.Role);

        // Return response
        var authResponse = new AuthResponse
        {
            UserId = newUser.Id,
            FirstName = newUser.FirstName,
            LastName = newUser.LastName,
            Email = newUser.Email!,
            Role = newUser.Role,
            AccessToken = token,
            RefreshToken = newUser.RefreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, $"User registered successfully with role {command.Role}");
    }
}
