using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

public interface IOptionSetCatalogService
{
    Task<OptionSetPageDto> SearchAsync(OptionSetKind? kind, string? query, string? cursor, int limit, CancellationToken cancellationToken);
    Task<OptionSetDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OptionSetDetailDto> CreateAsync(OptionSetWriteRequestDto request, CancellationToken cancellationToken);
    Task<OptionSetDetailDto> UpdateAsync(Guid id, int expectedVersion, OptionSetWriteRequestDto request, CancellationToken cancellationToken);
    Task<CreateOrReuseImportedSetResult> CreateOrReuseImportedSetAsync(CreateOrReuseImportedSetRequest request, CancellationToken cancellationToken);
}
