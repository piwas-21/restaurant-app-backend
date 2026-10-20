namespace RestaurantSystem.Api.Features.Addresses.Interfaces;

public interface IAddressContent
{
    string Label { get; }
    string AddressLine1 { get; }
    string? AddressLine2 { get; }
    string City { get; }
    string? State { get; }
    string PostalCode { get; }
    string Country { get; }
    string? Phone { get; }
    string? DeliveryInstructions { get; }
}
