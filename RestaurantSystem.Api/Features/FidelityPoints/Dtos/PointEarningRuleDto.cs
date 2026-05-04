namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public class PointEarningRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int PointsAwarded { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePointEarningRuleDto
{
    public string Name { get; set; } = string.Empty;
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int PointsAwarded { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 0;
}

public class UpdatePointEarningRuleDto
{
    public string Name { get; set; } = string.Empty;
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int PointsAwarded { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
}
