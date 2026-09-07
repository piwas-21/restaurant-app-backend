using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

public interface IBulkImageUploadWalker
{
    Task<(List<ProductImageDto> Uploaded, List<string> Errors)> UploadEachAsync(
        UploadMultipleProductImagesCommand command,
        Product product,
        bool hasPrimaryImage,
        int currentMaxSortOrder,
        CancellationToken cancellationToken);
}
