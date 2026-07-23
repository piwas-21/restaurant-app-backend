namespace RestaurantSystem.Api.Features.Settings.FormFields.Dtos;

/// <summary>
/// One field change in the PUT api/FormFieldConfiguration bulk update. Only
/// registry-known (FormKey, FieldKey) pairs are accepted; locked fields may only be
/// echoed back unchanged (visible + required). DisplayOrder is registry-driven and
/// not updatable.
/// </summary>
public sealed record UpdateFormFieldConfigurationDto(
    string FormKey,
    string FieldKey,
    bool IsVisible,
    bool IsRequired);
