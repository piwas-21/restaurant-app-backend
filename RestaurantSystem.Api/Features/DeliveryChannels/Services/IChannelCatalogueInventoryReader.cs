using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelCatalogueInventoryReader
{
    Task<ChannelCatalogueInventorySnapshot> Read(string provider, string storeId, string currency,
        bool isSandbox, string language, CancellationToken cancellationToken);

    Task<ChannelCatalogueSelectionSnapshot> ReadSelection(ChannelCatalogueSelectionSnapshotRequest request,
        string provider, string storeId, string currency, bool isSandbox, string language,
        CancellationToken cancellationToken);
}
