using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Commands.DeleteAddressCommand;

public record DeleteAddressCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteAddressCommandHandler : ICommandHandler<DeleteAddressCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteAddressCommandHandler> _logger;

    public DeleteAddressCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteAddressCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteAddressCommand command, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<string>.Failure("User not authenticated");
        }

        var address = await _context.UserAddresses
            .FirstOrDefaultAsync(a => a.Id == command.Id && a.UserId == userId.Value && !a.IsDeleted, cancellationToken);

        if (address == null)
        {
            return ApiResponse<string>.Failure("Address not found");
        }

        // Soft delete
        address.IsDeleted = true;
        address.DeletedAt = DateTime.UtcNow;
        address.DeletedBy = userId.Value.ToString();

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Address {AddressId} deleted successfully for user {UserId}", command.Id, userId.Value);
        return ApiResponse<string>.SuccessWithData("Address deleted successfully");
    }
}
