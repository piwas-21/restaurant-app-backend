namespace RestaurantSystem.Api.Features.Settings.Dtos;

public class WorkingHoursDto
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
}

public class UpdateWorkingHoursDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
}
