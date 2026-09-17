using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.DeleteMenuBundleCommand;

public record DeleteMenuBundleCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteMenuBundleCommandHandler : ICommandHandler<DeleteMenuBundleCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteMenuBundleCommandHandler> _logger;

    public DeleteMenuBundleCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteMenuBundleCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteMenuBundleCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Relationship validation and the soft delete share one serializable transaction. A
            // concurrent link/type/component writer therefore cannot pass the check on stale state
            // and leave a relationship pointing at this archived bundle.
            var product = await _context.Products
                .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

            if (product == null)
            {
                return ApiResponse<string>.Failure("Menu bundle not found");
            }

            if (product.Type != ProductType.Menu)
            {
                return ApiResponse<string>.Failure("Product is not a menu bundle");
            }

            await MenuOfferLinkRules.EnsureCanDeactivateAsync(
                _context, product.Id, isActive: false, cancellationToken);

            product.IsDeleted = true;
            product.DeletedAt = DateTime.UtcNow;
            product.DeletedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Menu Bundle {ProductId} deleted successfully", product.Id);
            return ApiResponse<string>.SuccessWithData("Menu bundle deleted successfully");
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "Transaction rollback failed during menu bundle delete");
            }

            MenuOfferLinkConflict.ThrowIfExpected(exception);
            throw;
        }
    }
}
