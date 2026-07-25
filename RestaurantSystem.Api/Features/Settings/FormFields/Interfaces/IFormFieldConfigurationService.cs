using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;

public interface IFormFieldConfigurationService
{
    /// <summary>
    /// Inserts a configuration row for every FormFieldRegistry entry that has none yet
    /// (registry defaults). Existing rows are never touched, so admin changes survive
    /// and newly registered fields self-heal — the OrderTypeConfiguration convention.
    /// </summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every registry field merged with its stored configuration, grouped per
    /// form in registry order. Locked fields are forced visible + required.
    /// </summary>
    Task<List<FormFieldsDto>> GetGroupedAsync(CancellationToken cancellationToken = default);
}
