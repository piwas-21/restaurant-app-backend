using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Queries.GetCurrentUserQuery;

public class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, ApiResponse<UserDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetCurrentUserQueryHandler> _logger;

    public GetCurrentUserQueryHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<GetCurrentUserQueryHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<UserDto>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;

        if (userId == null)
        {
            _logger.LogWarning("Attempt to get current user profile without authentication");
            return ApiResponse<UserDto>.Failure("User not authenticated");
        }

        var user = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = u.PhoneNumber,
                Role = u.Role.ToString(),
                IsEmailConfirmed = u.EmailConfirmed,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                Metadata = u.Metadata ?? new Dictionary<string, string>(),
                OrderLimitAmount = u.OrderLimitAmount,
                DiscountPercentage = u.DiscountPercentage,
                IsDiscountActive = u.IsDiscountActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", userId);
            return ApiResponse<UserDto>.Failure("User not found");
        }

        _logger.LogInformation("Retrieved profile for user {UserId}", userId);

        return ApiResponse<UserDto>.SuccessWithData(user, "User profile retrieved successfully");
    }
}
