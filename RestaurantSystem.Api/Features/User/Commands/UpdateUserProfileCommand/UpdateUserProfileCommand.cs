using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Commands.UpdateUserProfileCommand;

public record UpdateUserProfileCommand(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    Dictionary<string, string>? Metadata) : ICommand<ApiResponse<UserDto>>;
public class UpdateUserProfileCommandHandler : ICommandHandler<UpdateUserProfileCommand, ApiResponse<UserDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UpdateUserProfileCommandHandler> _logger;

    public UpdateUserProfileCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        ApplicationDbContext context,
        ILogger<UpdateUserProfileCommandHandler> logger)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<UserDto>> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
    {
        // Get current user
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null)
        {
            return ApiResponse<UserDto>.Failure("User not authenticated", "Authentication required");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == currentUserId.Value && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            return ApiResponse<UserDto>.Failure("User not found", "User does not exist");
        }

        // Update user profile
        user.FirstName = command.FirstName;
        user.LastName = command.LastName;

        if (!string.IsNullOrEmpty(command.PhoneNumber))
        {
            user.PhoneNumber = command.PhoneNumber;
        }

        if (command.Metadata != null)
        {
            user.Metadata = command.Metadata;
        }

        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = currentUserId.ToString();

        // Save changes
        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to update user profile for user {UserId}: {Errors}",
                currentUserId, errors);
            return ApiResponse<UserDto>.Failure("Failed to update profile", errors);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var userDto = MapToUserDto(user);

        _logger.LogInformation("User {UserId} updated their profile successfully", currentUserId);

        return ApiResponse<UserDto>.SuccessWithData(userDto, "Profile updated successfully");
    }

    private static UserDto MapToUserDto(ApplicationUser user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            IsEmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Metadata = user.Metadata,
            OrderLimitAmount = user.OrderLimitAmount,
            DiscountPercentage = user.DiscountPercentage,
            IsDiscountActive = user.IsDiscountActive
        };
    }
}
