using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Categories.Dtos;
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

    public UpdateCategoryImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UpdateCategoryImageCommandHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ApiResponse<CategoryDto>> Handle(UpdateCategoryImageCommand command, CancellationToken cancellationToken)
    {
        // Validate file
        if (command.Image == null || command.Image.Length == 0)
        {
            return ApiResponse<CategoryDto>.Failure("No image file provided");
        }

        // Validate file size
        var maxSizeBytes = _configuration.GetValue<long>("FileStorage:MaxFileSizeBytes", 5 * 1024 * 1024);
        if (command.Image.Length > maxSizeBytes)
        {
            return ApiResponse<CategoryDto>.Failure($"File size exceeds maximum allowed size of {maxSizeBytes / (1024 * 1024)}MB");
        }

        // Validate file type
        var allowedExtensions = _configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
            ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var fileExtension = Path.GetExtension(command.Image.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(fileExtension))
        {
            return ApiResponse<CategoryDto>.Failure($"File type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
        }

        // Validate MIME type
        var allowedMimeTypes = _configuration.GetSection("FileStorage:AllowedMimeTypes").Get<string[]>()
            ?? new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(command.Image.ContentType.ToLowerInvariant()))
        {
            return ApiResponse<CategoryDto>.Failure("Invalid image MIME type");
        }

        var category = await _context.Categories
            .Include(c => c.ProductCategories)
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
