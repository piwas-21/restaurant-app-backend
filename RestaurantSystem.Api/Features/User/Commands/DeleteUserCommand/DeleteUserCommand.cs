using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Commands.DeleteProductImageCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;

public record DeleteUserCommand(Guid UserId, bool Permanent = false) : ICommand<ApiResponse<string>>;

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        // Use IgnoreQueryFilters to find user even if already soft-deleted (for permanent delete)
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == command.UserId, cancellationToken);

        if (user == null)
        {
            return ApiResponse<string>.Failure("User not found");
        }

        bool shouldHardDelete = command.Permanent;

        if (user.Role == UserRole.Server || user.Role == UserRole.Cashier ||
            user.Role == UserRole.KitchenStaff || user.Role == UserRole.Admin)
        {
            shouldHardDelete = true;
        }

        if (shouldHardDelete)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Unlink Orders (set UserId to null)
                await _context.Orders
                    .Where(o => o.UserId == command.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.UserId, (Guid?)null), cancellationToken);

                // Unlink Reservations
                await _context.Reservations
                    .Where(r => r.CustomerId == command.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.CustomerId, (Guid?)null), cancellationToken);

                // Unlink Customer Discount Rules from Orders
                var userDiscountRuleIds = await _context.CustomerDiscountRules
                    .Where(r => r.UserId == command.UserId)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                if (userDiscountRuleIds.Any())
                {
                    await _context.Orders
                        .Where(o => o.CustomerDiscountRuleId.HasValue && userDiscountRuleIds.Contains(o.CustomerDiscountRuleId.Value))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.CustomerDiscountRuleId, (Guid?)null), cancellationToken);
                }

                // Delete dependencies (Hard Delete) - MUST IgnoreQueryFilters to ensure soft-deleted items are also removed
                await _context.Baskets.IgnoreQueryFilters().Where(x => x.UserId == command.UserId).ExecuteDeleteAsync(cancellationToken);
                await _context.UserAddresses.IgnoreQueryFilters().Where(x => x.UserId == command.UserId).ExecuteDeleteAsync(cancellationToken);
                await _context.FidelityPointBalances.IgnoreQueryFilters().Where(x => x.UserId == command.UserId).ExecuteDeleteAsync(cancellationToken);
                await _context.CustomerDiscountRules.IgnoreQueryFilters().Where(x => x.UserId == command.UserId).ExecuteDeleteAsync(cancellationToken);

                // Hard delete the user
                await _context.Users.IgnoreQueryFilters().Where(u => u.Id == command.UserId).ExecuteDeleteAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("User {UserId} ({Role}) and related data permanently deleted by user {DeletedBy}",
                    command.UserId, user.Role, _currentUserService.UserId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error permanently deleting user {UserId}", command.UserId);
                throw;
            }
        }
        else
        {
            if (user.IsDeleted)
            {
                return ApiResponse<string>.Failure("User is already deleted");
            }

            // Soft delete for customers
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.DeletedBy = _currentUserService.GetAuditIdentifier();

            _logger.LogInformation("Customer {UserId} soft deleted by user {DeletedBy}",
                command.UserId, _currentUserService.UserId);

            await _context.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<string>.SuccessWithData(shouldHardDelete ? "User permanently deleted" : "User deleted successfully");
    }
}
