namespace RestaurantSystem.Api.Features.Settings.FormFields.Dtos;

/// <summary>
/// One customer form field as served by GET api/FormFieldConfiguration.
/// <c>IsLocked</c> comes from the server-side registry: locked fields are always
/// visible + required and rejected on PUT. For checkout_contact.phone the frontend
/// must still OR in its per-order-type rule (Takeaway/Delivery require a phone) —
/// <c>IsRequired</c> here is only the admin-configured part.
/// </summary>
public sealed record FormFieldConfigurationDto(
    string FieldKey,
    bool IsVisible,
    bool IsRequired,
    bool IsLocked,
    int DisplayOrder);

/// <summary>All fields of one customer-facing form, ordered by DisplayOrder.</summary>
public sealed record FormFieldsDto(
    string FormKey,
    List<FormFieldConfigurationDto> Fields);
