using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

/// <summary>
/// Stores a batch of product images, keeping every per-file rejection reason and returning it to
/// the caller. The response contract lives on the record. Per-file staging belongs to
/// <see cref="BulkImageUploadWalker"/>; this handler owns validation of the batch shape and the
/// single transaction around the walk.
/// </summary>
public class UploadMultipleProductImagesCommandHandler(
    ApplicationDbContext context,
    ILogger<UploadMultipleProductImagesCommandHandler> logger,
    IBulkImageUploadWalker bulkImageUploadWalker)
    : ICommandHandler<UploadMultipleProductImagesCommand, ApiResponse<List<ProductImageDto>>>
{
    public async Task<ApiResponse<List<ProductImageDto>>> Handle(UploadMultipleProductImagesCommand command, CancellationToken cancellationToken)
    {
        if (command.Images == null || command.Images.Count == 0)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("No image files provided");
        }

        var product = await context.Products
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

        using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            (uploadedImages, errors) = await bulkImageUploadWalker.UploadEachAsync(
                command, product, hasPrimaryImage, currentMaxSortOrder, cancellationToken);

            if (uploadedImages.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
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
            logger.LogError(ex, "Failed to complete bulk upload for product {ProductId}", command.ProductId);
            return ApiResponse<List<ProductImageDto>>.Failure("Failed to upload images");
        }

        if (errors.Count == 0)
        {
            logger.LogInformation("Bulk upload of {Count} images completed successfully for product {ProductId}",
                uploadedImages.Count, command.ProductId);

            return ApiResponse<List<ProductImageDto>>.SuccessWithData(
                uploadedImages, $"Successfully uploaded {uploadedImages.Count} images");
        }

        logger.LogWarning("Bulk upload completed with errors for product {ProductId}: {Errors}",
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
}
