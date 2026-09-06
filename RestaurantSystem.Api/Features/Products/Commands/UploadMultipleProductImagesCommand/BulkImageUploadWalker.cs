using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

/// <summary>
/// Walks the incoming files of a bulk upload: rule-screen each, store the acceptable ones, and
/// turn every failure into an entry in <c>errors</c> rather than aborting the batch. Kept in its
/// own service so the command handler stays inside its CLAUDE.md §4 file-length budget and the
/// transaction flow stays separately readable.
/// </summary>
public sealed class BulkImageUploadWalker(
    ApplicationDbContext context,
    IFileStorageService fileStorageService,
    IImageProcessor imageProcessor,
    ICurrentUserService currentUserService,
    IConfiguration configuration,
    IOptions<FileStorageSettings> fileStorageSettings,
    ILogger<BulkImageUploadWalker> logger)
{
    private readonly ApplicationDbContext _context = context;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly IImageProcessor _imageProcessor = imageProcessor;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    private readonly FileStorageSettings _fileStorageSettings = fileStorageSettings.Value;
    private readonly ILogger<BulkImageUploadWalker> _logger = logger;

    public async Task<(List<ProductImageDto> Uploaded, List<string> Errors)> UploadEachAsync(
        UploadMultipleProductImagesCommand command,
        Product product,
        bool hasPrimaryImage,
        int currentMaxSortOrder,
        CancellationToken cancellationToken)
    {
        var uploadedImages = new List<ProductImageDto>();
        var errors = new List<string>();

        foreach (var image in command.Images)
        {
            if (!ImageUploadRules.IsAcceptable(image, _fileStorageSettings, out var rejection))
            {
                errors.Add(Describe(image, rejection));
                continue;
            }

            try
            {
                var isPrimary = !hasPrimaryImage;
                uploadedImages.Add(await StoreAsync(
                    command.ProductId, product.Name, image, isPrimary, ++currentMaxSortOrder, cancellationToken));
                hasPrimaryImage |= isPrimary;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload image '{FileName}' for product {ProductId}",
                    image.FileName, command.ProductId);
                errors.Add(Describe(image, "the file could not be stored"));
            }
        }

        return (uploadedImages, errors);
    }

    /// <summary>
    /// Uploads one file and stages its <see cref="ProductImage"/> row; the caller commits.
    /// </summary>
    private async Task<ProductImageDto> StoreAsync(
        Guid productId,
        string productName,
        IFormFile image,
        bool isPrimary,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var imageUrl = await _fileStorageService.UploadFileAsync(
            image, $"products/{productId}", cancellationToken: cancellationToken);

        // Best-effort card variant, same contract as the single upload: a failed derivation
        // leaves CardUrl null and the guest serves the original.
        await using var originalSource = image.OpenReadStream();
        var cardUrl = await ProductImageCardVariants.GenerateAndStoreAsync(
            _fileStorageService, _imageProcessor,
            $"products/{productId}", Path.GetFileName(imageUrl),
            originalSource, _logger, cancellationToken);

        var productImage = new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Url = imageUrl,
            CardUrl = cardUrl,
            AltText = productName,
            IsPrimary = isPrimary,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.ProductImages.Add(productImage);

        return new ProductImageDto
        {
            Id = productImage.Id,
            Url = UrlJoin.Join(_baseUrl, productImage.Url),
            CardUrl = productImage.CardUrl is null ? null : UrlJoin.Join(_baseUrl, productImage.CardUrl),
            AltText = productImage.AltText,
            IsPrimary = productImage.IsPrimary,
            SortOrder = productImage.SortOrder,
            ProductId = productImage.ProductId
        };
    }

    /// <summary>
    /// The user-facing reason a single file was not stored, named so the user can tell which of
    /// the photos they picked is missing.
    /// </summary>
    private static string Describe(IFormFile image, string reason) =>
        $"'{image.FileName}' — {reason}";
}
