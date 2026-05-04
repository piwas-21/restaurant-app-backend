using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadProductImageCommand;

public record UploadProductImageCommand : ICommand<ApiResponse<ProductImageDto>>
{
    public Guid ProductId { get; init; }
    public IFormFile Image { get; init; } = null!;
    public string? AltText { get; init; }
    public bool IsPrimary { get; init; }
    public int? SortOrder { get; init; }
}


public class UploadProductImageCommandHandler : ICommandHandler<UploadProductImageCommand, ApiResponse<ProductImageDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UploadProductImageCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;

    public UploadProductImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UploadProductImageCommandHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _configuration = configuration;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<ProductImageDto>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        // Validate file
        if (command.Image == null || command.Image.Length == 0)
        {
            return ApiResponse<ProductImageDto>.Failure("No image file provided");
        }

        // Validate file size
        var maxSizeBytes = _configuration.GetValue<long>("FileStorage:MaxFileSizeBytes", 5 * 1024 * 1024);
        if (command.Image.Length > maxSizeBytes)
        {
            return ApiResponse<ProductImageDto>.Failure($"File size exceeds maximum allowed size of {maxSizeBytes / (1024 * 1024)}MB");
        }

        // Validate file type
        var allowedExtensions = _configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
            ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var fileExtension = Path.GetExtension(command.Image.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(fileExtension))
        {
            return ApiResponse<ProductImageDto>.Failure($"File type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
        }

        // Validate MIME type
        var allowedMimeTypes = _configuration.GetSection("FileStorage:AllowedMimeTypes").Get<string[]>()
            ?? new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(command.Image.ContentType.ToLowerInvariant()))
        {
            return ApiResponse<ProductImageDto>.Failure("Invalid image MIME type");
        }

        // Check if product exists
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<ProductImageDto>.Failure("Product not found");
        }

        try
        {
            // Upload image to storage
            var imageUrl = await _fileStorageService.UploadFileAsync(
                command.Image,
                $"products/{command.ProductId}",
                cancellationToken: cancellationToken);

            // If this is the first image or marked as primary, unset other primary images
            if (command.IsPrimary || !product.Images.Any(i => !i.IsDeleted))
            {
                var existingPrimaryImages = product.Images.Where(i => i.IsPrimary && !i.IsDeleted);
                foreach (var img in existingPrimaryImages)
                {
                    img.IsPrimary = false;
                    img.UpdatedAt = DateTime.UtcNow;
                    img.UpdatedBy = _currentUserService.GetAuditIdentifier();
                }
            }

            // Create image record
            var productImage = new ProductImage
            {
                ProductId = command.ProductId,
                Url = imageUrl,
                AltText = command.AltText ?? product.Name,
                IsPrimary = command.IsPrimary || !product.Images.Any(i => !i.IsDeleted),
                SortOrder = command.SortOrder ?? product.Images.Count(i => !i.IsDeleted),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.ProductImages.Add(productImage);
            await _context.SaveChangesAsync(cancellationToken);

            var responseDto = new ProductImageDto
            {
                Url = _baseUrl + "/" + productImage.Url,
                AltText = productImage.AltText,
                IsPrimary = productImage.IsPrimary,
                SortOrder = productImage.SortOrder,
                ProductId = productImage.ProductId
            };

            _logger.LogInformation("Image uploaded successfully for product {ProductId} by user {UserId}",
                command.ProductId, _currentUserService.UserId);

            return ApiResponse<ProductImageDto>.SuccessWithData(responseDto, "Image uploaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image for product {ProductId}", command.ProductId);
            return ApiResponse<ProductImageDto>.Failure("Failed to upload image");
        }
    }
}
