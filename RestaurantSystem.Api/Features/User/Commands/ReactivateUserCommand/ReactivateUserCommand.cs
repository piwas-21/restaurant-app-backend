using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Commands.ReactivateUserCommand;

public record ReactivateUserCommand(Guid UserId) : ICommand<ApiResponse<string>>;

public class ReactivateUserCommandHandler : ICommandHandler<ReactivateUserCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ReactivateUserCommandHandler> _logger;

    public ReactivateUserCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<ReactivateUserCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        // We need to query using IgnoreQueryFilters() to find soft-deleted users
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user == null)
        {
            return ApiResponse<string>.Failure("User not found");
        }

        if (!user.IsDeleted)
        {
            return ApiResponse<string>.Failure("User is already active");
        }

        user.IsDeleted = false;
        user.DeletedAt = null;
        user.DeletedBy = null;

        _logger.LogInformation("User {UserId} reactivated by user {ReactivatedBy}",
            command.UserId, _currentUserService.UserId);

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<string>.SuccessWithData("User reactivated successfully");
    }
}
