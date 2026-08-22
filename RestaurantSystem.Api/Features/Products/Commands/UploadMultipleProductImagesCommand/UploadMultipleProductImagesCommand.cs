using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

public record UploadMultipleProductImagesCommand(
    Guid ProductId,
    List<IFormFile> Images
) : ICommand<ApiResponse<List<ProductImageDto>>>;

/// <summary>
/// Stores a batch of product images, keeping every per-file rejection reason and returning it to
/// the caller.
/// </summary>
/// <remarks>
/// The response contract, decided in Track F1b after a tenant lost weeks of photo uploads to it.
/// <b>Nothing stored</b> ⇒ <c>Failure</c> with one <c>Errors</c> entry per rejected file; it used to
/// be a <c>SuccessWithData</c> carrying an empty list and <c>errors: null</c>, so a total failure was
/// indistinguishable from a no-op on the wire and the reason lived only in the server log.
/// <b>Some stored, some rejected</b> ⇒ still a success envelope (the stored images ARE saved and the
/// client must render them) but with <c>Errors</c> populated, so the user can be told WHICH photo did
/// not make it. <b>All stored</b> ⇒ success, <c>Errors</c> null. The HTTP status stays 200 in every
/// case: the controller is a plain <c>Ok(result)</c> like every other endpoint here, and clients
/// branch on the envelope's <c>success</c> flag.
/// </remarks>
public class UploadMultipleProductImagesCommandHandler : ICommandHandler<UploadMultipleProductImagesCommand, ApiResponse<List<ProductImageDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UploadMultipleProductImagesCommandHandler> _logger;
    private readonly FileStorageSettings _fileStorageSettings;
    private readonly string _baseUrl;

    public UploadMultipleProductImagesCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UploadMultipleProductImagesCommandHandler> logger,
        IConfiguration configuration,
        IOptions<FileStorageSettings> fileStorageSettings)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _fileStorageSettings = fileStorageSettings.Value;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<List<ProductImageDto>>> Handle(UploadMultipleProductImagesCommand command, CancellationToken cancellationToken)
    {
        if (command.Images == null || command.Images.Count == 0)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("No image files provided");
        }

        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == command.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("Product not found");
        }

        var uploadedImages = new List<ProductImageDto>();
        var errors = new List<string>();

        var currentMaxSortOrder = product.Images.Any(i => !i.IsDeleted)
            ? product.Images.Where(i => !i.IsDeleted).Max(i => i.SortOrder)
            : -1;

        // Set first image as primary if no primary exists
        var hasPrimaryImage = product.Images.Any(i => !i.IsDeleted && i.IsPrimary);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload image '{FileName}' for product {ProductId}",
                        image.FileName, command.ProductId);
                    errors.Add(Describe(image, "the file could not be stored"));
                }
            }

            if (uploadedImages.Count > 0)
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

        if (errors.Count == 0)
        {
            _logger.LogInformation("Bulk upload of {Count} images completed successfully for product {ProductId}",
                uploadedImages.Count, command.ProductId);

            return ApiResponse<List<ProductImageDto>>.SuccessWithData(
                uploadedImages, $"Successfully uploaded {uploadedImages.Count} images");
        }

        _logger.LogWarning("Bulk upload completed with errors for product {ProductId}: {Errors}",
            command.ProductId, string.Join(", ", errors));

        if (uploadedImages.Count == 0)
        {
            return ApiResponse<List<ProductImageDto>>.Failure(
                errors, $"None of the {errors.Count} files could be uploaded.");
        }

        var partial = ApiResponse<List<ProductImageDto>>.SuccessWithData(
            uploadedImages, $"Uploaded {uploadedImages.Count} images. {errors.Count} failed.");
        partial.Errors = errors;
        return partial;
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

        var productImage = new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Url = imageUrl,
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
