using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;

public record UpdateCategoryCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder
) : ICommand<ApiResponse<CategoryDto>>;

public class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand, ApiResponse<CategoryDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateCategoryCommandHandler> _logger;

    public UpdateCategoryCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateCategoryCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .Include(c => c.ProductCategories)
            .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            return ApiResponse<CategoryDto>.Failure("Category not found");
        }

        // Check if another category with the same name exists (case-insensitive)
        var duplicateCategory = await _context.Categories
            .Where(c => !c.IsDeleted && c.Id != command.Id)
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, command.Name), cancellationToken);

        if (duplicateCategory != null)
        {
            return ApiResponse<CategoryDto>.Failure("Another category with this name already exists");
        }

        category.Name = command.Name;
        category.Description = command.Description;
        category.IsActive = command.IsActive;
        category.UpdatedAt = DateTime.UtcNow;
        category.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var categoryDto = new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            ProductCount = category.ProductCategories.Count(pc => !pc.Product.IsDeleted && pc.Product.IsActive),
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };

        _logger.LogInformation("Category {CategoryId} updated successfully", category.Id);
        return ApiResponse<CategoryDto>.SuccessWithData(categoryDto, "Category updated successfully");
    }
}
