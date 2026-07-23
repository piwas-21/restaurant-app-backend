namespace RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;

public interface IFormFieldRequirementService
{
    /// <summary>
    /// Enforces admin-configured requiredness for a customer form. <paramref name="fieldValues"/>
    /// maps registry field keys to the submitted values — pass only the fields whose
    /// requiredness is config-driven (locked fields are enforced by DataAnnotations on the
    /// request DTO); fields absent from the dictionary are not checked. Throws
    /// <see cref="Common.Exceptions.BadRequestException"/> naming every configured-required
    /// field that is null or whitespace. Reads configuration rows and falls back to registry
    /// defaults for rows not yet seeded — never writes on this (customer-facing) path.
    /// </summary>
    Task EnsureRequiredFieldsPresentAsync(
        string formKey,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken cancellationToken = default);
}
