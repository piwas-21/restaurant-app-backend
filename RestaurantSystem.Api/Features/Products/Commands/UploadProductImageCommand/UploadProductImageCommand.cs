using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
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
    private readonly IImageProcessor _imageProcessor;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UploadProductImageCommandHandler> _logger;
    private readonly string _baseUrl;
    private readonly FileStorageSettings _fileStorageSettings;

    public UploadProductImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        IImageProcessor imageProcessor,
        ICurrentUserService currentUserService,
        ILogger<UploadProductImageCommandHandler> logger,
        IConfiguration configuration,
        IOptions<FileStorageSettings> fileStorageSettings)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
        _fileStorageSettings = fileStorageSettings.Value;
        _imageProcessor = imageProcessor;
    }

    public async Task<ApiResponse<ProductImageDto>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        if (!ImageUploadRules.IsAcceptable(command.Image, _fileStorageSettings, out var rejection))
        {
            return ApiResponse<ProductImageDto>.Failure(rejection);
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

            // The card variant is best-effort (fail-open to serving the original): a failed
            // derivation must never fail the upload itself.
            var cardUrl = await ProductImageCardVariants.GenerateAndStoreAsync(
                _fileStorageService, _imageProcessor,
                $"products/{command.ProductId}", Path.GetFileName(imageUrl),
                command.Image.OpenReadStream(), _logger, cancellationToken);

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
                CardUrl = cardUrl,
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
                Url = UrlJoin.Join(_baseUrl, productImage.Url),
                CardUrl = productImage.CardUrl is null ? null : UrlJoin.Join(_baseUrl, productImage.CardUrl),
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
