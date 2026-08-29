namespace RestaurantSystem.Api.Features.Settings.Dtos;

/// <summary>
/// One serving window inside a day: <c>11:00-15:00</c>. A day holds a list of these, which is what
/// lets a restaurant say it shuts between lunch and dinner instead of publishing 11:00-23:00.
/// </summary>
public class WorkingHoursShiftDto
{
    public TimeSpan OpenTime { get; set; }

    public TimeSpan CloseTime { get; set; }
}
