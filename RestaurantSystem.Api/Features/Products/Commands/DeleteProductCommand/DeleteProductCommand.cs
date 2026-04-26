using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
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

        var product = await _context.Products
            .Include(c => c.ProductCategories)
            .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<string>.Failure("Product not found");
        }

        // Soft delete
        product.IsDeleted = true;
        product.DeletedAt = DateTime.UtcNow;
        product.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Product {ProductId} deleted successfully", product.Id);
        return ApiResponse<string>.SuccessWithData("Product deleted successfully");
    }
}
