using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.OptionSets.Search;
using RestaurantSystem.Api.Features.OptionSets.Services;

namespace RestaurantSystem.Api.Features.OptionSets;

public static class OptionSetServiceCollectionExtensions
{
    public static IServiceCollection AddOptionSetFeatures(this IServiceCollection services)
    {
        services.AddScoped<IOptionSetCatalogService, OptionSetCatalogService>();
        services.AddScoped<IOptionSetMaterializer, OptionSetMaterializer>();
        services.AddScoped<IOptionSetMaterializationJobService, OptionSetMaterializationJobService>();
        services.AddScoped<IOptionSetMaterializationJobRunner, OptionSetMaterializationJobRunner>();
        services.AddScoped<IMenuAuthoringSearchService, MenuAuthoringSearchService>();
        services.AddHostedService<OptionSetMaterializationJobWorker>();
        return services;
    }
}
