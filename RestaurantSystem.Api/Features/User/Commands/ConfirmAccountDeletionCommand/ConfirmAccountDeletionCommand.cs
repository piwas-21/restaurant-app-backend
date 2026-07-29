using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RestaurantSystem.Api.Features.User.Commands.ConfirmAccountDeletionCommand;

public record ConfirmAccountDeletionCommand(Guid UserId, string Token) : ICommand<ApiResponse<string>>;

public class ConfirmAccountDeletionCommandHandler : ICommandHandler<ConfirmAccountDeletionCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ConfirmAccountDeletionCommandHandler> _logger;

    public ConfirmAccountDeletionCommandHandler(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ILogger<ConfirmAccountDeletionCommandHandler> logger)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(ConfirmAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            return ApiResponse<string>.Failure("User not found or already deleted");
        }

        var isValid = await _userManager.VerifyUserTokenAsync(user, "Default", "AccountDeletion", command.Token);
        if (!isValid)
        {
            return ApiResponse<string>.Failure("Invalid or expired deletion token");
        }

        // --- Cleanup dependent entities before deleting user ---
        try
        {
            // 1. Delete Baskets (Soft Delete or Hard Delete depending on strategy - using Hard as baskets are transient)
            // INCLUDE Soft-deleted baskets to avoid FK errors
            await _context.Baskets
                .IgnoreQueryFilters()
                .Where(b => b.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // 2. Delete UserAddresses (Cannot exist without user)
            // INCLUDE Soft-deleted addresses
            await _context.UserAddresses
                .IgnoreQueryFilters()
                .Where(a => a.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // 3. Delete FidelityPointBalance (User specific)
            await _context.FidelityPointBalances
                .Where(f => f.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // 4. Delete FidelityPointsTransactions (User specific)
            await _context.FidelityPointsTransactions
                .Where(t => t.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // 5. Delete GroupMemberships (User specific)
            await _context.GroupMemberships
                .Where(g => g.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // 6. Anonymize Orders (Set UserId to null)
            // CRITICAL: INCLUDE Soft-deleted orders to avoid FK violations
            await _context.Orders
                .IgnoreQueryFilters()
                .Where(o => o.UserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, (Guid?)null), cancellationToken);

            // 7. Anonymize Reservations (Set CustomerId to null)
            await _context.Reservations
                .Where(r => r.CustomerId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CustomerId, (Guid?)null), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up dependent entities for user {UserId}", user.Id);
            return ApiResponse<string>.Failure("An error occurred while cleaning up user data.");
        }
        // -----------------------------------------------------

        // soft-delete-bypass: GDPR Art. 17 erasure. This MUST remove the row, not flag it —
        // ApplicationUser is ISoftDelete AND IExcludeFromGlobalFilter, so a flagged row keeps every
        // piece of PII on it (email, names, phone, password hash, metadata) and is not even hidden
        // by a query filter; its unfiltered unique indexes on Email/NormalizedEmail/NormalizedUserName
        // would also burn that address forever, so the customer could never re-register.
        //
        // Deliberately NOT _userManager.DeleteAsync: that calls _context.Remove(user) internally, and
        // ApplyAuditInformation now converts a Remove of an ISoftDelete entity into a soft delete —
        // which would silently turn this erasure into a no-op. ExecuteDeleteAsync bypasses the change
        // tracker, so erasure no longer depends on ASP.NET Identity's internals at all. This also
        // makes all three deletion paths consistent: DeleteUserCommand and AccountCleanupService
        // already erase this way.
        var deleted = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            _logger.LogError("Failed to delete user {UserId}: no row was removed", user.Id);
            return ApiResponse<string>.Failure("Failed to delete account.");
        }

        _logger.LogInformation("User {UserId} permanently deleted via confirmation token", user.Id);

        return ApiResponse<string>.SuccessWithData("Account permanently deleted");
    }
}
