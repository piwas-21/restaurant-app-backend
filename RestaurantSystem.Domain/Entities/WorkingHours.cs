using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class WorkingHours : Entity
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsClosed { get; set; } = false; // For special closed days
    public string? Notes { get; set; }
}
