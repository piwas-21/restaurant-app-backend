using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Settings.Dtos;

namespace RestaurantSystem.Api.Features.Settings.Services;

/// <summary>
/// Turns an <see cref="UpdateWorkingHoursDto"/> into the day's serving windows, or refuses it.
/// Lifted out of <c>WorkingHoursService</c> so the rules can be read — and tested — without a
/// database, and so the service stays a service.
/// </summary>
public static class WorkingHoursShiftRules
{
    /// <summary>
    /// A sanity ceiling, not a business rule: a day has a breakfast, a lunch, a tea and a dinner at
    /// the very most. It exists so an unbounded list cannot be posted at the endpoint.
    /// </summary>
    public const int MaxShiftsPerDay = 4;

    private static readonly TimeSpan EndOfDay = TimeSpan.FromHours(24);

    /// <summary>
    /// The windows to store, ordered by <c>OpenTime</c>.
    /// <para>
    /// A day marked <c>IsClosed</c> is not validated and keeps whatever windows it is sent. That is
    /// deliberate: an admin who toggles Monday shut and saves must get Monday's hours back when
    /// they toggle it open again, so the times are stored and simply never read while the day is
    /// closed.
    /// </para>
    /// </summary>
    /// <exception cref="BadRequestException">The day is open and its windows are not usable.</exception>
    public static List<WorkingHoursShiftDto> Resolve(UpdateWorkingHoursDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Shifts == null means "this caller predates the field", so its single pair IS the day.
        // Shifts == [] means "this caller knows about the field and sent nothing", which is only
        // legal on a closed day — see the DTO for why those two must not collapse into one.
        var shifts = dto.Shifts is null
            ? [new WorkingHoursShiftDto { OpenTime = dto.OpenTime, CloseTime = dto.CloseTime }]
            : dto.Shifts.OrderBy(s => s.OpenTime).ToList();

        if (dto.IsClosed)
        {
            return shifts;
        }

        if (shifts.Count == 0)
        {
            throw new BadRequestException(
                $"{dto.DayOfWeek} is not marked closed, so it needs at least one opening window.");
        }

        if (shifts.Count > MaxShiftsPerDay)
        {
            throw new BadRequestException(
                $"{dto.DayOfWeek} has {shifts.Count} opening windows; at most {MaxShiftsPerDay} are allowed.");
        }

        for (var i = 0; i < shifts.Count; i++)
        {
            var shift = shifts[i];

            if (shift.OpenTime < TimeSpan.Zero || shift.CloseTime > EndOfDay)
            {
                throw new BadRequestException(
                    $"An opening window on {dto.DayOfWeek} falls outside the day.");
            }

            // Equal is refused too: a zero-length window is never what anyone meant, and it would
            // report the shop as open for exactly one instant.
            if (shift.CloseTime <= shift.OpenTime)
            {
                throw new BadRequestException(
                    $"Closing time must be after opening time on {dto.DayOfWeek} " +
                    $"({shift.OpenTime:hh\\:mm}-{shift.CloseTime:hh\\:mm}).");
            }

            // Touching windows are legal (11:00-15:00 then 15:00-23:00 is one long service split
            // for staffing); overlapping ones are not, because the day would then have no single
            // answer to "when does lunch end".
            if (i > 0 && shift.OpenTime < shifts[i - 1].CloseTime)
            {
                throw new BadRequestException(
                    $"Opening windows on {dto.DayOfWeek} overlap " +
                    $"({shifts[i - 1].OpenTime:hh\\:mm}-{shifts[i - 1].CloseTime:hh\\:mm} and " +
                    $"{shift.OpenTime:hh\\:mm}-{shift.CloseTime:hh\\:mm}).");
            }
        }

        return shifts;
    }
}
