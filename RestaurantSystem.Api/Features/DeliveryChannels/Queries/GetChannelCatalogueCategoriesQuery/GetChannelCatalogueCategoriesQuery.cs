using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoriesQuery;

public sealed record GetChannelCatalogueCategoriesQuery : IQuery<ChannelCatalogueCategoriesSnapshot>;

public sealed class GetChannelCatalogueCategoriesQueryHandler(IChannelCatalogueInventoryReader inventory,
    IOptions<DeliveryChannelSettings> options, IEmailLanguageResolver languages)
    : IQueryHandler<GetChannelCatalogueCategoriesQuery, ChannelCatalogueCategoriesSnapshot>
{
    public async Task<ChannelCatalogueCategoriesSnapshot> Handle(GetChannelCatalogueCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        var binding = ChannelCatalogueBinding.Require(options.Value);
        var source = await inventory.Read(binding.Provider, binding.StoreId, binding.Currency, true,
            languages.TenantDefault, cancellationToken);
        return new(source.Provider, source.StoreId, source.Currency, source.IsSandbox, source.Language,
            source.Revision, source.Categories);
    }
}

internal static class ChannelCatalogueBinding
{
    public static ChannelCatalogueBindingValue Require(DeliveryChannelSettings settings)
    {
        var bindings = settings.Stores.Where(row => row.Provider == "uber-eats" && row.IsSandbox).ToArray();
        if (!settings.Enabled || bindings.Length != 1)
            throw new RestaurantSystem.Api.Common.Exceptions.ForbiddenException("This marketplace store is not enabled for this tenant.");
        var binding = bindings[0];
        return new(binding.Provider, binding.StoreId, binding.Currency);
    }
}

internal sealed record ChannelCatalogueBindingValue(string Provider, string StoreId, string Currency);
