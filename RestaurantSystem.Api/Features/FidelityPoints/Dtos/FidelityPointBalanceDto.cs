namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public class FidelityPointBalanceDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CurrentPoints { get; set; }
    public int TotalEarnedPoints { get; set; }
    public int TotalRedeemedPoints { get; set; }
    public DateTime LastUpdated { get; set; }
    public decimal CurrentPointsValue => CurrentPoints / 100m; // 100 points = $1
}
