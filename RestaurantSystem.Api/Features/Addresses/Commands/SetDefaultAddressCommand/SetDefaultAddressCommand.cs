using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Commands.SetDefaultAddressCommand;

public record SetDefaultAddressCommand(Guid AddressId) : ICommand<ApiResponse<string>>;

public class SetDefaultAddressCommandHandler : ICommandHandler<SetDefaultAddressCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SetDefaultAddressCommandHandler> _logger;

    public SetDefaultAddressCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<SetDefaultAddressCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(SetDefaultAddressCommand command, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<string>.Failure("User not authenticated");
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Verify address exists and belongs to user
            var address = await _context.UserAddresses
                .FirstOrDefaultAsync(a => a.Id == command.AddressId && a.UserId == userId.Value && !a.IsDeleted, cancellationToken);

            if (address == null)
            {
                return ApiResponse<string>.Failure("Address not found");
            }

            // Unset all other default addresses for this user
            await _context.UserAddresses
                .Where(a => a.UserId == userId.Value && a.IsDefault && a.Id != command.AddressId && !a.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.IsDefault, false)
                    .SetProperty(a => a.UpdatedAt, DateTime.UtcNow)
                    .SetProperty(a => a.UpdatedBy, userId.Value.ToString()),
                    cancellationToken);

            // Set this address as default
            address.IsDefault = true;
            address.UpdatedAt = DateTime.UtcNow;
            address.UpdatedBy = userId.Value.ToString();

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Address {AddressId} set as default for user {UserId}", command.AddressId, userId.Value);
            return ApiResponse<string>.SuccessWithData("Default address updated successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error setting default address {AddressId} for user {UserId}", command.AddressId, userId.Value);
            throw;
        }
    }
}
