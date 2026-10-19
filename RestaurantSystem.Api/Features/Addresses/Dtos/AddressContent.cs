namespace RestaurantSystem.Api.Features.Addresses.Dtos;

public abstract record AddressContent : AddressTextContent
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    internal bool RequireCustomer { get; init; }
}
