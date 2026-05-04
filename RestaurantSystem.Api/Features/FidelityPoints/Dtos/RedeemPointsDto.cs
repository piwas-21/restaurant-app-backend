namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public class RedeemPointsRequestDto
{
    public int PointsToRedeem { get; set; }
}

public class RedeemPointsResponseDto
{
    public Guid TransactionId { get; set; }
    public int PointsRedeemed { get; set; }
    public decimal DiscountAmount { get; set; }
    public int RemainingPoints { get; set; }
    public string Message { get; set; } = string.Empty;
}
