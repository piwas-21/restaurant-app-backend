using System.Net.Http;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.Api.Features.Catalogue;

public static class CentralCatalogueExtensions
{
    public static IServiceCollection AddCentralCatalogue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CentralCatalogueSettings>()
            .Bind(configuration.GetSection(CentralCatalogueSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ICentralCatalogueClient, CentralCatalogueClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<CentralCatalogueSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<ICatalogueCuisinePreferencesService, CatalogueCuisinePreferencesService>();
        services.AddScoped<ICatalogueTemplateQueryService, CatalogueTemplateQueryService>();
        services.AddScoped<ICatalogueTemplateGraphLoader, CatalogueTemplateGraphLoader>();
        services.AddScoped<ICatalogueImportStateStore, CatalogueImportStateStore>();
        services.AddScoped<ICatalogueImportPreviewService, CatalogueImportPreviewService>();
        services.AddScoped<ICatalogueTemplateImportExecutor, CatalogueTemplateImportExecutor>();
        services.AddScoped<ICatalogueImportLock, CatalogueImportLock>();
        services.AddScoped<ICatalogueSessionImporter, CatalogueSessionImporter>();
        services.AddScoped<ICatalogueRejectedCandidateService, CatalogueRejectedCandidateService>();
        services.AddScoped<ICatalogueImportSessionService, CatalogueImportSessionService>();

        return services;
    }
}
