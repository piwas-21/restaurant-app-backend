namespace RestaurantSystem.Api.Features.Settings.FormFields;

/// <summary>
/// Server-side definition of one customer-facing form field. Locked fields are always
/// visible + required (the admin cannot change them — they anchor the flow, e.g. the
/// reservation confirmation email needs the customer's email address).
/// </summary>
public sealed record FormFieldDefinition(
    string FormKey,
    string FieldKey,
    bool IsLocked,
    bool DefaultIsVisible,
    bool DefaultIsRequired,
    int DisplayOrder);

/// <summary>
/// The single source of truth for which (formKey, fieldKey) pairs exist, which are
/// locked, and their default visibility/requiredness. Database rows are seeded from
/// this registry on first read (insert-missing, so newly added fields self-heal) and
/// every PUT is validated against it. Defaults mirror today's frontend behaviour.
/// </summary>
public static class FormFieldRegistry
{
    public static class FormKeys
    {
        public const string Reservation = "reservation";
        public const string CheckoutContact = "checkout_contact";
        public const string DeliveryAddress = "delivery_address";
    }

    public static class ReservationFields
    {
        public const string CustomerName = "customerName";
        public const string CustomerEmail = "customerEmail";
        public const string CustomerPhone = "customerPhone";
        public const string SpecialRequests = "specialRequests";
    }

    public static class CheckoutContactFields
    {
        public const string Name = "name";
        public const string Email = "email";
        public const string Phone = "phone";
    }

    public static class DeliveryAddressFields
    {
        public const string Street = "street";
        public const string PostalCode = "postalCode";
        public const string City = "city";
        public const string Country = "country";
        public const string AdditionalInfo = "additionalInfo";
    }

    public static readonly IReadOnlyList<FormFieldDefinition> Fields =
    [
        Locked(FormKeys.Reservation, ReservationFields.CustomerName, 0),
        Locked(FormKeys.Reservation, ReservationFields.CustomerEmail, 1),
        Optional(FormKeys.Reservation, ReservationFields.CustomerPhone, 2),
        Optional(FormKeys.Reservation, ReservationFields.SpecialRequests, 3),

        Locked(FormKeys.CheckoutContact, CheckoutContactFields.Name, 0),
        Locked(FormKeys.CheckoutContact, CheckoutContactFields.Email, 1),
        // NOTE: effective requiredness of checkout phone = config-required OR the
        // frontend's per-order-type rule (Takeaway/Delivery require a phone number);
        // that merge is computed frontend-side — see FormFieldConfigurationDto.
        Optional(FormKeys.CheckoutContact, CheckoutContactFields.Phone, 2),

        Locked(FormKeys.DeliveryAddress, DeliveryAddressFields.Street, 0),
        Locked(FormKeys.DeliveryAddress, DeliveryAddressFields.PostalCode, 1),
        Locked(FormKeys.DeliveryAddress, DeliveryAddressFields.City, 2),
        Optional(FormKeys.DeliveryAddress, DeliveryAddressFields.Country, 3),
        Optional(FormKeys.DeliveryAddress, DeliveryAddressFields.AdditionalInfo, 4),
    ];

    public static FormFieldDefinition? Find(string formKey, string fieldKey) =>
        Fields.FirstOrDefault(f => f.FormKey == formKey && f.FieldKey == fieldKey);

    private static FormFieldDefinition Locked(string formKey, string fieldKey, int displayOrder) =>
        new(formKey, fieldKey, IsLocked: true, DefaultIsVisible: true, DefaultIsRequired: true, displayOrder);

    private static FormFieldDefinition Optional(string formKey, string fieldKey, int displayOrder) =>
        new(formKey, fieldKey, IsLocked: false, DefaultIsVisible: true, DefaultIsRequired: false, displayOrder);
}
