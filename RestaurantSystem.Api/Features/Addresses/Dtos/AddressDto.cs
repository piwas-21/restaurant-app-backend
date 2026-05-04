namespace RestaurantSystem.Api.Features.Addresses.Dtos;

public record AddressDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Label { get; set; } = null!;
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? DeliveryInstructions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
