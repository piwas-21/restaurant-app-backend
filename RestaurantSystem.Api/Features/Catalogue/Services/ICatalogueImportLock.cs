namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueImportLock
{
    Task<IAsyncDisposable> AcquireAsync(string scope, CancellationToken cancellationToken);
}
