namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record TableQRCodeDto
{
    public Guid TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public string QRCodeData { get; set; } = string.Empty;
    public DateTime QRCodeGeneratedAt { get; set; }
    public string QRCodeUrl { get; set; } = string.Empty;
}
