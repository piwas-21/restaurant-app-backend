namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public interface IOptionSetMaterializationJobService
{
    Task<OptionSetMaterializationJobDto> CreateAsync(
        Guid optionSetId,
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken);

    Task<OptionSetMaterializationJobDto> GetAsync(
        Guid optionSetId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task<OptionSetMaterializationJobDto> ResumeAsync(
        Guid optionSetId,
        Guid jobId,
        CancellationToken cancellationToken);
}
