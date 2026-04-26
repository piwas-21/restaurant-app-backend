namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record CreateOrderDeliveryAddressDto
{
    public Guid? UseAddressId { get; set; } // If provided, fetch from user's saved addresses
    public string? Label { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? DeliveryInstructions { get; set; }
}
