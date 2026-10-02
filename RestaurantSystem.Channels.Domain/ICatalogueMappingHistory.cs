namespace RestaurantSystem.Channels.Domain;

public interface ICatalogueMappingHistory
{
    Task<CataloguePublication?> FindVerified(AvailabilityBinding binding, string catalogueRevision, CancellationToken cancellationToken);
}
