using FluentValidation.Validators;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;

public record CreateCategoryCommand(
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder
) : ICommand<ApiResponse<CategoryDto>>;

public class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, ApiResponse<CategoryDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateCategoryCommandHandler> _logger;

    public CreateCategoryCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<CreateCategoryCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Check if category with same name exists (case-insensitive)
        var existingCategory = await _context.Categories
            .Where(c => !c.IsDeleted)
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, command.Name), cancellationToken);

        if (existingCategory != null)
        {
            return ApiResponse<CategoryDto>.Failure("Category with this name already exists");
        }

        var max = await _context.Categories
            .Where(c => !c.IsDeleted)
            .MaxAsync(c => (int?)c.DisplayOrder, cancellationToken) ?? 0;

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            IsActive = command.IsActive,
            DisplayOrder = max + 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        var categoryDto = new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            ProductCount = 0,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };

        _logger.LogInformation("Category {CategoryId} created successfully", category.Id);
        return ApiResponse<CategoryDto>.SuccessWithData(categoryDto, "Category created successfully");
    }
}
