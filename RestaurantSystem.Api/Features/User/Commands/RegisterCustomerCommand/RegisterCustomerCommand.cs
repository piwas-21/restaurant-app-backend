using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
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
    string ConfirmPassword) : ICommand<ApiResponse<AuthResponse>>
{
    // Set only by the strict native command; legacy JSON cannot choose a different delivery rail.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NativeClient { get; init; }
}

public class RegisterCustomerCommandHandler : ICommandHandler<RegisterCustomerCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshSessionService _sessions;
    private readonly IEmailService _emailService;
    private readonly ICustomerNativeAccountMail _nativeMail;
    private readonly IEmailLanguageResolver _languages;
    private readonly ILogger<RegisterCustomerCommandHandler> _logger;

    public RegisterCustomerCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IRefreshSessionService sessions,
        IEmailService emailService,
        ICustomerNativeAccountMail nativeMail,
        IEmailLanguageResolver languages,
        ILogger<RegisterCustomerCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _sessions = sessions;
        _emailService = emailService;
        _nativeMail = nativeMail;
        _languages = languages;
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
            // What this request asked for, or nothing. Unlike an order — which must always carry a
            // language, because its mails are sent from paths with no request — an account records
            // only what the person actually expressed, so an absent header stays null and every
            // later mail falls through to the request or the tenant default (§1 rank 2).
            PreferredLanguage = _languages.FromRequest(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System",
            // Legacy columns stay empty for compatibility with already-deployed rows.
            RefreshToken = string.Empty
        };

        var result = await _userManager.CreateAsync(newUser, command.Password);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("Customer registration failed for email {Email}: {Errors}", command.Email, string.Join(", ", errors));
            return ApiResponse<AuthResponse>.Failure(errors, "Failed to create user");
        }

        // Generate tokens. The raw refresh token only leaves in this response; the database gets its hash.
        var token = command.NativeClient ? string.Empty : _tokenService.GenerateAccessToken(newUser);
        var refreshToken = command.NativeClient ? string.Empty : await _sessions.IssueAsync(newUser, cancellationToken);
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
        Exception? mailFailure = null;
        try
        {
            // The language this registration was made in, which the row above just recorded — the
            // one mail whose recipient asked for it moments ago, in this very request.
            if (command.NativeClient)
                await _nativeMail.SendAsync(_languages.ForAccount(newUser), newUser, verificationToken, CustomerNativeAccountLinks.Verify);
            else
                await _emailService.SendEmailVerificationAsync(_languages.ForAccount(newUser), newUser, verificationToken);
        }
        catch (Exception ex)
        {
            mailFailure = ex;
        }
        if (mailFailure is not null)
        {
            if (command.NativeClient)
                _logger.LogWarning("Native registration mail failed with {ExceptionType}", mailFailure.GetType().Name);
            else
                _logger.LogError(mailFailure, "Failed to send welcome email to user {UserId}", newUser.Id);
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
            RefreshToken = refreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, "Customer registered successfully");
    }
}
