using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

public record UploadMultipleProductImagesCommand(
    Guid ProductId,
    List<IFormFile> Images
) : ICommand<ApiResponse<List<ProductImageDto>>>;

public class UploadMultipleProductImagesCommandHandler : ICommandHandler<UploadMultipleProductImagesCommand, ApiResponse<List<ProductImageDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UploadMultipleProductImagesCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;

    public UploadMultipleProductImagesCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UploadMultipleProductImagesCommandHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _configuration = configuration;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<List<ProductImageDto>>> Handle(UploadMultipleProductImagesCommand command, CancellationToken cancellationToken)
    {
        if (command.Images == null || command.Images.Count == 0)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("No image files provided");
        }

        // Check if product exists
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("Product not found");
        }

        var uploadedImages = new List<ProductImageDto>();
        var errors = new List<string>();
        var maxSizeBytes = _configuration.GetValue<long>("FileStorage:MaxFileSizeBytes", 5 * 1024 * 1024);
        var allowedExtensions = _configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
            ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var allowedMimeTypes = _configuration.GetSection("FileStorage:AllowedMimeTypes").Get<string[]>()
            ?? new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };

        var currentMaxSortOrder = product.Images.Any(i => !i.IsDeleted)
            ? product.Images.Where(i => !i.IsDeleted).Max(i => i.SortOrder)
            : -1;

        // Set first image as primary if no primary exists
        var hasPrimaryImage = product.Images.Any(i => !i.IsDeleted && i.IsPrimary);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            for (int i = 0; i < command.Images.Count; i++)
            {
                var image = command.Images[i];

                // Validate each file
                if (image.Length == 0)
                {
                    errors.Add($"File '{image.FileName}' is empty");
                    continue;
                }

                if (image.Length > maxSizeBytes)
                {
                    errors.Add($"File '{image.FileName}' exceeds maximum size");
                    continue;
                }

                var fileExtension = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    errors.Add($"File '{image.FileName}' has invalid extension");
                    continue;
                }

                if (!allowedMimeTypes.Contains(image.ContentType.ToLowerInvariant()))
                {
                    errors.Add($"File '{image.FileName}' has invalid MIME type");
                    continue;
                }

                try
                {
                    // Upload image
                    string imageUrl;
                    try
                    {
                        imageUrl = await _fileStorageService.UploadFileAsync(
                           image,
                           $"products/{command.ProductId}",
                           cancellationToken: cancellationToken);

                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex.Message, ex);
                        throw;
                    }

                    // Create image record
                    var imageId = Guid.NewGuid();
                    var productImage = new ProductImage
                    {
                        Id = imageId,
                        ProductId = command.ProductId,
                        Url = imageUrl,
                        AltText = product.Name,
                        IsPrimary = !hasPrimaryImage && i == 0, // First image becomes primary if none exists
                        SortOrder = ++currentMaxSortOrder,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };

                    _context.ProductImages.Add(productImage);

                    uploadedImages.Add(new ProductImageDto
                    {
                        Id = imageId,
                        Url = _baseUrl + "/" + productImage.Url,
                        AltText = productImage.AltText,
                        IsPrimary = productImage.IsPrimary,
                        SortOrder = productImage.SortOrder,
                        ProductId = productImage.ProductId
                    });

                    if (!hasPrimaryImage && i == 0)
                    {
                        hasPrimaryImage = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload image '{FileName}' for product {ProductId}",
                        image.FileName, command.ProductId);
                    errors.Add($"Failed to upload '{image.FileName}'");
                }
            }

            if (uploadedImages.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to complete bulk upload for product {ProductId}", command.ProductId);
            return ApiResponse<List<ProductImageDto>>.Failure("Failed to upload images");
        }

        if (errors.Any())
        {
            _logger.LogWarning("Bulk upload completed with errors for product {ProductId}: {Errors}",
                command.ProductId, string.Join(", ", errors));

            return ApiResponse<List<ProductImageDto>>.SuccessWithData(
                uploadedImages,
                $"Uploaded {uploadedImages.Count} images. {errors.Count} failed.");
        }

        _logger.LogInformation("Bulk upload of {Count} images completed successfully for product {ProductId}",
            uploadedImages.Count, command.ProductId);

        return ApiResponse<List<ProductImageDto>>.SuccessWithData(
            uploadedImages,
            $"Successfully uploaded {uploadedImages.Count} images");
    }
}
