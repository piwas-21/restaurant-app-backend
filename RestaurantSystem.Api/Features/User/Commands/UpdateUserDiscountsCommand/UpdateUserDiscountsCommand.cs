using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Commands.UpdateUserDiscountsCommand;

public record UpdateUserDiscountsCommand(
    Guid UserId,
    decimal OrderLimitAmount,
    decimal DiscountPercentage,
    bool IsDiscountActive) : ICommand<ApiResponse<UserDto>>;

public class UpdateUserDiscountsCommandHandler : ICommandHandler<UpdateUserDiscountsCommand, ApiResponse<UserDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UpdateUserDiscountsCommandHandler> _logger;

    public UpdateUserDiscountsCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        ApplicationDbContext context,
        ILogger<UpdateUserDiscountsCommandHandler> logger)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<UserDto>> Handle(UpdateUserDiscountsCommand command, CancellationToken cancellationToken)
    {
        // Check if current user is admin
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || currentUser.Role != UserRole.Admin)
        {
            return ApiResponse<UserDto>.Failure("Unauthorized access", "Only administrators can update user discount settings");
        }

        // Get target user
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            return ApiResponse<UserDto>.Failure("User not found", "The specified user does not exist");
        }

        // Update discount settings
        user.OrderLimitAmount = command.OrderLimitAmount;
        user.DiscountPercentage = command.DiscountPercentage;
        user.IsDiscountActive = command.IsDiscountActive;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = currentUser.Id.ToString();

        // Save changes
        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to update discount settings for user {UserId}: {Errors}",
                command.UserId, errors);
            return ApiResponse<UserDto>.Failure("Failed to update discount settings", errors);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var userDto = MapToUserDto(user);

        _logger.LogInformation("Admin {AdminId} updated discount settings for user {UserId}. " +
            "OrderLimit: {OrderLimit}, Discount: {Discount}%, Active: {IsActive}",
            currentUser.Id, command.UserId, command.OrderLimitAmount,
            command.DiscountPercentage, command.IsDiscountActive);

        return ApiResponse<UserDto>.SuccessWithData(userDto, "Discount settings updated successfully");
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
