using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IExternalOrderImporter
{
    Task<ExternalOrderImportDto> ImportAsync(ExternalOrderRequest request, CancellationToken cancellationToken);
}
