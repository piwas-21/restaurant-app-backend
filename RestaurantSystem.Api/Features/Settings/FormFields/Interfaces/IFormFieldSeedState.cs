namespace RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;

/// <summary>
/// Per-app-instance marker that the FormFieldRegistry rows have been seeded, so the
/// per-request key scan in <see cref="IFormFieldConfigurationService.EnsureSeededAsync"/>
/// short-circuits after the first success. Singleton; never reset — the registry only
/// changes with a deploy, and a new app instance starts unseeded again.
/// </summary>
public interface IFormFieldSeedState
{
    bool IsSeeded { get; }

    void MarkSeeded();
}
