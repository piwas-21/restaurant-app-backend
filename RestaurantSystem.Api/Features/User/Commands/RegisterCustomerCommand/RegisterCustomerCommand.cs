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
            return ApiResponse<AuthResponse>.FailureWithCode(
                "User with this email already exists",
                ErrorCodes.EmailAlreadyExists,
                "Registration failed");
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
        newUser.RefreshTokenExpiryTime = _tokenService.GetRefreshTokenExpiration();
        // Registration sends the verification mail below, so it opens the per-address cooldown
        // too (GAP-3) — otherwise register-then-resend would deliver two mails back to back and
        // the cooldown would only start on the second. Piggy-backs the update already happening.
        newUser.LastEmailVerificationSentAt = DateTime.UtcNow;
        var stamped = await _userManager.UpdateAsync(newUser);
        if (!stamped.Succeeded)
        {
            // Not fatal — the account exists and the mail below still goes out — but if this
            // update is lost so is the cooldown, and the register screen's own resend button then
            // delivers a second identical mail. Silence here would make that look like a bug in
            // the cooldown rather than in this write.
            _logger.LogWarning(
                "Post-registration update failed for user {UserId}: {Errors}",
                newUser.Id, string.Join(", ", stamped.Errors.Select(e => e.Code)));
        }

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
            Email = newUser.Email,
            Role = newUser.Role,
            AccessToken = token,
            RefreshToken = newUser.RefreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, "Customer registered successfully");
    }
}
