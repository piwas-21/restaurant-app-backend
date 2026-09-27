namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public interface IOptionSetMaterializer
{
    Task<OptionSetMaterializationPreview> PreviewAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken);

    Task<OptionSetMaterializationResult> ApplyAsync(
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken);

    Task<OptionSetMaterializationResult> ApplyImportedAsync(
        OptionSetMaterializationRequest request,
        IReadOnlySet<Guid> stagedProductIds,
        CancellationToken cancellationToken);

    Task<CreateOrReuseImportedSetResult> CreateOrReuseImportedSetAsync(
        CreateOrReuseImportedSetRequest request,
        CancellationToken cancellationToken);
}
