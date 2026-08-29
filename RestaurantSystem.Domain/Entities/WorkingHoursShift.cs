using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One contiguous serving window inside a day — <c>11:00-15:00</c>, or <c>18:00-23:00</c>.
/// <para>
/// A day owns N of these. That is the whole point: <see cref="WorkingHours"/> held exactly one
/// open/close pair, so a restaurant that shuts between lunch and dinner could only publish
/// <c>11:00-23:00</c>, which tells a customer the shop is serving at 16:00 when it is dark.
/// </para>
/// <para>
/// Shifts carry ONLY the window. <c>IsActive</c>, <c>IsClosed</c> and <c>Notes</c> stay on the day
/// row, because they are facts about the DAY: "closed on Monday" is not a property of one of
/// Monday's two services.
/// </para>
/// </summary>
public class WorkingHoursShift : Entity
{
    public Guid WorkingHoursId { get; set; }

    public WorkingHours WorkingHours { get; set; } = null!;

    public TimeSpan OpenTime { get; set; }

    public TimeSpan CloseTime { get; set; }
}
