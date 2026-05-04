using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.DeleteCategoryCommand;

public record DeleteCategoryCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteCategoryCommandHandler> _logger;

    public DeleteCategoryCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteCategoryCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {

        var category = await _context.Categories
            .Include(c => c.ProductCategories)
                .ThenInclude(pc => pc.Product) // Add this line to include the Product entity
            .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            return ApiResponse<string>.Failure("Category not found");
        }

        if (category.ProductCategories.Any(pc => pc.Product != null && !pc.Product.IsDeleted))
        {
            return ApiResponse<string>.Failure("Cannot delete category with associated products. Please remove all products from this category first.");
        }

        // Soft delete
        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        category.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Category {CategoryId} deleted successfully", category.Id);
        return ApiResponse<string>.SuccessWithData("Category deleted successfully");
    }
}
