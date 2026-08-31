using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Commands.UpdateStaffCommand;

public record UpdateStaffCommand(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("firstName")] string FirstName,
    [property: JsonPropertyName("lastName")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("role")] UserRole Role) : ICommand<ApiResponse<AuthResponse>>;

public class UpdateStaffCommandHandler : ICommandHandler<UpdateStaffCommand, ApiResponse<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IRefreshSessionService _sessions;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateStaffCommandHandler> _logger;

    public UpdateStaffCommandHandler(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ITokenService tokenService,
        IRefreshSessionService sessions,
        ICurrentUserService currentUserService,
        ILogger<UpdateStaffCommandHandler> logger)
    {
        _userManager = userManager;
        _context = context;
        _tokenService = tokenService;
        _sessions = sessions;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(UpdateStaffCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"UpdateStaffCommand received - UserId: {command.UserId}, Email: {command.Email}, FirstName: {command.FirstName}");

        // Check if current user is admin (this endpoint should be admin-only)
        var currentUser = await _currentUserService.GetUserAsync();

        if (currentUser == null || currentUser.Role != UserRole.Admin)
        {
            return ApiResponse<AuthResponse>.Failure("Unauthorized access", "Only administrators can update staff users");
        }

        // Find user by ID (ignoring soft delete filter)
        _logger.LogInformation($"Attempting to find user with ID: {command.UserId}");

        // Debug: Count all users
        var totalUsers = await _context.Users.IgnoreQueryFilters().CountAsync(cancellationToken);
        _logger.LogInformation($"Total users in database (ignoring filters): {totalUsers}");

        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (existingUser == null)
        {
            _logger.LogWarning($"User not found with ID: {command.UserId}");

            // Debug: List all user IDs
            var allUserIds = await _context.Users
                .IgnoreQueryFilters()
                .Select(u => u.Id)
                .Take(10)
                .ToListAsync(cancellationToken);
            _logger.LogWarning($"First 10 user IDs in database: {string.Join(", ", allUserIds)}");

            return ApiResponse<AuthResponse>.Failure("User doesn't exist", "Update failed");
        }

        _logger.LogInformation($"Found user: {existingUser.Email}, updating...");

        // Update basic info
        existingUser.FirstName = command.FirstName;
        existingUser.LastName = command.LastName;
        existingUser.UserName = command.FirstName;
        existingUser.PhoneNumber = command.PhoneNumber;
        existingUser.Role = command.Role;

        // Every `IdentityResult` below used to be discarded, so a refused change was reported as a
        // successful one. That is not theoretical for the password: `StrongPasswordValidator`
        // (Program.cs:173) adds a repeated-character rule and a common-password list that the
        // FluentValidation rules do NOT have, so e.g. "Aa1!aaaa" passes every rule in
        // `PasswordRules`, is rejected by Identity for the "aaaa" run, and the admin was told the
        // password had been changed while it stayed as it was.
        if (existingUser.Email != command.Email)
        {
            var emailToken = await _userManager.GenerateChangeEmailTokenAsync(existingUser, command.Email);
            var emailResult = await _userManager.ChangeEmailAsync(existingUser, command.Email, emailToken);
            if (!emailResult.Succeeded)
            {
                return IdentityFailure(emailResult, "Email could not be updated");
            }
        }

        // Update password only if provided
        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            string resetToken = await _userManager.GeneratePasswordResetTokenAsync(existingUser);
            var passwordResult = await _userManager.ResetPasswordAsync(existingUser, resetToken, command.Password);
            if (!passwordResult.Succeeded)
            {
                return IdentityFailure(passwordResult, "Password could not be updated");
            }
        }

        var updateResult = await _userManager.UpdateAsync(existingUser);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult, "Update failed");
        }

        // Issue a fresh session so the caller gets a new, usable pair; a separate refresh session
        // per issuance is what keeps the staff member's other logged-in browsers working.
        var accessToken = _tokenService.GenerateAccessToken(existingUser);
        var rawRefreshToken = await _sessions.IssueAsync(existingUser, cancellationToken);

        var authResponse = new AuthResponse
        {
            UserId = existingUser.Id,
            FirstName = existingUser.FirstName,
            LastName = existingUser.LastName,
            Email = existingUser.Email!,
            Role = existingUser.Role,
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        // Said "registered" on an UPDATE — copied from RegisterStaffCommand. Any client that shows
        // the server's own success text told the admin they had just created the user they edited.
        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, $"User updated successfully with role {command.Role}");
    }

    /// <summary>
    /// Identity's own reasons, as a list — the shape that reaches the client as a real
    /// multi-entry <c>errors[]</c> (`RegisterStaffCommand` does the same). Note this is NOT the
    /// shape a FluentValidation failure takes: those are joined into one string long before here
    /// (issue #291).
    /// </summary>
    private static ApiResponse<AuthResponse> IdentityFailure(IdentityResult result, string message) =>
        ApiResponse<AuthResponse>.Failure(
            result.Errors.Select(e => e.Description).ToList(),
            message);
}
