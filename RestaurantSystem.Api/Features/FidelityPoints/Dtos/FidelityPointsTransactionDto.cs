namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public class FidelityPointsTransactionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? OrderId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int Points { get; set; }
    public decimal? OrderTotal { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
