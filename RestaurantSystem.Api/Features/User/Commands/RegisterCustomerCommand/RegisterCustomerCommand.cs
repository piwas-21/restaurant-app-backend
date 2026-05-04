using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.User.Commands.RegisterCustomerCommand;

public record RegisterCustomerCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string ConfirmPassword) : ICommand<ApiResponse<AuthResponse>>;

public class RegisterCustomerCommandHandler : ICommandHandler<RegisterCustomerCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegisterCustomerCommandHandler> _logger;

    public RegisterCustomerCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEmailService emailService,
        ILogger<RegisterCustomerCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(command.Email);
        if (existingUser != null)
        {
            return ApiResponse<AuthResponse>.Failure("User with this email already exists", "Registration failed");
        }

        // Create new customer user
        var newUser = new ApplicationUser
        {
            Email = command.Email,
            UserName = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Role = UserRole.Customer, // Always customer for public registration
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System",
            RefreshToken = _tokenService.GenerateRefreshToken()
        };

        var result = await _userManager.CreateAsync(newUser, command.Password);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Customer registration failed for email {Email}: {Errors}", command.Email, string.Join(", ", errors));
            return ApiResponse<AuthResponse>.Failure(errors, "Failed to create user");
        }

        // Generate tokens
        var token = _tokenService.GenerateAccessToken(newUser);
        newUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(newUser);

        // Generate email verification token
        var verificationToken = await _userManager.GenerateEmailConfirmationTokenAsync(newUser);

        // Send verification email
        try
        {
            await _emailService.SendEmailVerificationAsync(newUser, verificationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to user {UserId}", newUser.Id);
            // Don't fail the registration if email sending fails
        }

        _logger.LogInformation("Customer {UserId} successfully registered", newUser.Id);

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

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, "Customer registered successfully");
    }
}
