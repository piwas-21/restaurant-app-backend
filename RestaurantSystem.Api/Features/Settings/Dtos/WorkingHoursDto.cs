namespace RestaurantSystem.Api.Features.Settings.Dtos;

public class WorkingHoursDto
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>
    /// The FIRST shift's opening time, ordered by <c>OpenTime</c> — NOT the earliest thing that was
    /// typed, and NOT the day's overall span. Kept for clients written before <see cref="Shifts"/>
    /// existed; new callers read <see cref="Shifts"/>, which is the only field that can express a
    /// lunch/dinner split.
    /// </summary>
    public TimeSpan OpenTime { get; set; }

    /// <summary>The FIRST shift's closing time. See <see cref="OpenTime"/>.</summary>
    public TimeSpan CloseTime { get; set; }

    /// <summary>Every serving window of the day, ordered by <c>OpenTime</c>.</summary>
    public List<WorkingHoursShiftDto> Shifts { get; set; } = [];

    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
}

public class UpdateWorkingHoursDto
{
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>
    /// Legacy single-shift opening time. Read ONLY when <see cref="Shifts"/> is <c>null</c>.
    /// </summary>
    public TimeSpan OpenTime { get; set; }

    /// <summary>Legacy single-shift closing time. See <see cref="OpenTime"/>.</summary>
    public TimeSpan CloseTime { get; set; }

    /// <summary>
    /// The day's serving windows. <b>Nullable on purpose, and the two empty values mean different
    /// things.</b>
    /// <list type="bullet">
    /// <item><c>null</c> — the caller does not know about shifts at all (a client written against
    /// the older contract, or the mobile app). Its <see cref="OpenTime"/>/<see cref="CloseTime"/>
    /// are taken as one shift, so an old body keeps doing exactly what it used to do.</item>
    /// <item><c>[]</c> — the caller knows about shifts and sent none. On a day that is not
    /// <see cref="IsClosed"/> that is a <c>400</c>, never a silent close: "the field was omitted"
    /// and "the restaurant serves nobody that day" must not be the same payload.</item>
    /// </list>
    /// </summary>
    public List<WorkingHoursShiftDto>? Shifts { get; set; }

    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
}
