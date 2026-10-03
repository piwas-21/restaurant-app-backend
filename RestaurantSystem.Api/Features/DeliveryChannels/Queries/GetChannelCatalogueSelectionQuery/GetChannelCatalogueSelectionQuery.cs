using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoriesQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueSelectionQuery;

public sealed record GetChannelCatalogueSelectionQuery(ChannelCatalogueSelectionSnapshotRequest Request)
    : IQuery<ChannelCatalogueSelectionSnapshot>;

public sealed class GetChannelCatalogueSelectionQueryHandler(IChannelCatalogueInventoryReader inventory,
    IOptions<DeliveryChannelSettings> options, IEmailLanguageResolver languages)
    : IQueryHandler<GetChannelCatalogueSelectionQuery, ChannelCatalogueSelectionSnapshot>
{
    public Task<ChannelCatalogueSelectionSnapshot> Handle(GetChannelCatalogueSelectionQuery query,
        CancellationToken cancellationToken)
    {
        var binding = ChannelCatalogueBinding.Require(options.Value);
        return inventory.ReadSelection(query.Request, binding.Provider, binding.StoreId, binding.Currency, true,
            languages.TenantDefault, cancellationToken);
    }
}
