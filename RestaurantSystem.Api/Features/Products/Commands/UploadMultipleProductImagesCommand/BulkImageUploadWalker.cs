using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

/// <summary>
/// Walks the incoming files of a bulk upload: rule-screen each, store the acceptable ones, and
/// turn every failure into an entry in <c>errors</c> rather than aborting the batch. Kept beside
/// the handler so the transaction flow and the per-file rules stay separately readable (and the
/// handler inside its CLAUDE.md §4 file-length budget).
/// </summary>
internal static class BulkImageUploadWalker
{
    public static async Task<(List<ProductImageDto> Uploaded, List<string> Errors)> UploadEachAsync(
        UploadMultipleProductImagesCommand command,
        Product product,
        bool hasPrimaryImage,
        int currentMaxSortOrder,
        FileStorageSettings fileStorageSettings,
        ILogger<UploadMultipleProductImagesCommandHandler> logger,
        Func<Guid, string, IFormFile, bool, int, CancellationToken, Task<ProductImageDto>> storeAsync,
        Func<IFormFile, string, string> describe,
        CancellationToken cancellationToken)
    {
        var uploadedImages = new List<ProductImageDto>();
        var errors = new List<string>();

        foreach (var image in command.Images)
        {
            if (!ImageUploadRules.IsAcceptable(image, fileStorageSettings, out var rejection))
            {
                errors.Add(describe(image, rejection));
                continue;
            }

            try
            {
                var isPrimary = !hasPrimaryImage;
                uploadedImages.Add(await storeAsync(
                    command.ProductId, product.Name, image, isPrimary, ++currentMaxSortOrder, cancellationToken));
                hasPrimaryImage |= isPrimary;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload image '{FileName}' for product {ProductId}",
                    image.FileName, command.ProductId);
                errors.Add(describe(image, "the file could not be stored"));
            }
        }

        return (uploadedImages, errors);
    }
}
