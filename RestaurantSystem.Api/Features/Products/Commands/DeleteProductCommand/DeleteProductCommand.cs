using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.DeleteProductCommand;

public record DeleteProductCommand(Guid Id) : ICommand<ApiResponse<string>>;
public class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteProductCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Load and validate inside the same serializable transaction as the soft delete. This
            // makes a concurrent link/type/component writer either serialize before this decision
            // or receive a clean retryable conflict instead of preserving a stale relationship.
            var product = await _context.Products
                .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

            if (product == null)
            {
                return ApiResponse<string>.Failure("Product not found");
            }

            await MenuOfferLinkRules.EnsureCanDeactivateAsync(
                _context, product.Id, isActive: false, cancellationToken);

            product.IsDeleted = true;
            product.DeletedAt = DateTime.UtcNow;
            product.DeletedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Product {ProductId} deleted successfully", product.Id);
            return ApiResponse<string>.SuccessWithData("Product deleted successfully");
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "Transaction rollback failed during product delete");
            }

            MenuOfferLinkConflict.ThrowIfExpected(exception);
            throw;
        }
    }
}
