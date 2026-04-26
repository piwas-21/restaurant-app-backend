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

namespace RestaurantSystem.Api.Features.User.Commands.EditStaffCommand;

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
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly ILogger<UpdateStaffCommandHandler> _logger;

    public UpdateStaffCommandHandler(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ITokenService tokenService,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        ILogger<UpdateStaffCommandHandler> logger)
    {
        _userManager = userManager;
        _context = context;
        _tokenService = tokenService;
        _currentUserService = currentUserService;
        _emailService = emailService;
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

        // Update email if changed
        if (existingUser.Email != command.Email)
        {
            var emailToken = await _userManager.GenerateChangeEmailTokenAsync(existingUser, command.Email);
            await _userManager.ChangeEmailAsync(existingUser, command.Email, emailToken);
        }

        // Update password only if provided
        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            string resetToken = await _userManager.GeneratePasswordResetTokenAsync(existingUser);
            await _userManager.ResetPasswordAsync(existingUser, resetToken, command.Password);
        }

        await _userManager.UpdateAsync(existingUser);

        // Generate tokens
        var token = _tokenService.GenerateAccessToken(existingUser);
        existingUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

        // Return response
        var authResponse = new AuthResponse
        {
            UserId = existingUser.Id,
            FirstName = existingUser.FirstName,
            LastName = existingUser.LastName,
            Email = existingUser.Email!,
            Role = existingUser.Role,
            AccessToken = token,
            RefreshToken = existingUser.RefreshToken,
            Expiration = _tokenService.GetAccessTokenExpiration()
        };

        return ApiResponse<AuthResponse>.SuccessWithData(authResponse, $"User registered successfully with role {command.Role}");
    }
}
