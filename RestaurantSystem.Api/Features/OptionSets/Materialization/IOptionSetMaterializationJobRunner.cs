namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public interface IOptionSetMaterializationJobRunner
{
    Task<bool> RunNextBatchAsync(CancellationToken cancellationToken);
}
