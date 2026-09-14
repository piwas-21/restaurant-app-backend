using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Products.Services;

public interface IProductCustomizationGroupSynchronizer
{
    Task SyncAsync(
        Product product,
        IReadOnlyCollection<ProductCustomizationGroupDto> incoming,
        string auditIdentifier,
        CancellationToken cancellationToken);
}
