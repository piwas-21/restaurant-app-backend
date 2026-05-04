namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record TableValidationDto
{
    public bool IsValid { get; set; }
    public Guid TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int MaxGuests { get; set; }
    public bool IsOutdoor { get; set; }
    public DateTime? QRCodeGeneratedAt { get; set; }
}
