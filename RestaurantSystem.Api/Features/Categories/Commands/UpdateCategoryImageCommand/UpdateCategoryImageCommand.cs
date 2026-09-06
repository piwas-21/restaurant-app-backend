using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryImageCommand;

public record UpdateCategoryImageCommand(
    Guid CategoryId,
    IFormFile Image
) : ICommand<ApiResponse<CategoryDto>>;


public class UpdateCategoryImageCommandHandler : ICommandHandler<UpdateCategoryImageCommand, ApiResponse<CategoryDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateCategoryImageCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileStorageSettings _fileStorageSettings;

    public UpdateCategoryImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UpdateCategoryImageCommandHandler> logger,
        IConfiguration configuration,
        IOptions<FileStorageSettings> fileStorageSettings)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _configuration = configuration;
        _fileStorageSettings = fileStorageSettings.Value;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(UpdateCategoryImageCommand command, CancellationToken cancellationToken)
    {
        if (!ImageUploadRules.IsAcceptable(command.Image, _fileStorageSettings, out var rejection))
        {
            return ApiResponse<CategoryDto>.Failure(rejection);
        }

        // See UpdateCategoryCommand: ProductCount dereferences `pc.Product` in memory after
        // materialisation, so without ThenInclude this 500s for any category that has products.
        var category = await _context.Categories
            .Include(c => c.ProductCategories)
                .ThenInclude(pc => pc.Product)
            .FirstOrDefaultAsync(c => c.Id == command.CategoryId && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            return ApiResponse<CategoryDto>.Failure("Category not found");
        }

        try
        {
            // Delete old image if exists
            if (!string.IsNullOrEmpty(category.ImageUrl))
            {
                await _fileStorageService.DeleteFileAsync(category.ImageUrl, cancellationToken);
            }

            // Upload new image
            var imageUrl = await _fileStorageService.UploadFileAsync(
                command.Image,
                $"categories/{command.CategoryId}",
                cancellationToken: cancellationToken);

            category.ImageUrl = imageUrl;
            category.UpdatedAt = DateTime.UtcNow;
            category.UpdatedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);

            var categoryDto = new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                ImageUrl = UrlJoin.Join(_configuration["AWS:S3:BaseUrl"], category.ImageUrl),
                IsActive = category.IsActive,
                DisplayOrder = category.DisplayOrder,
                IsHiddenFromAllTab = category.IsHiddenFromAllTab,
                AvailableOrderTypes = category.AvailableOrderTypes,
                ProductCount = category.ProductCategories.Count(pc => !pc.Product.IsDeleted && pc.Product.IsActive),
                CreatedAt = category.CreatedAt,
                UpdatedAt = category.UpdatedAt
            };

            _logger.LogInformation("Category {CategoryId} image updated successfully", category.Id);
            return ApiResponse<CategoryDto>.SuccessWithData(categoryDto, "Category image updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image for category {CategoryId}", command.CategoryId);
            return ApiResponse<CategoryDto>.Failure("Failed to upload image");
        }
    }
}
