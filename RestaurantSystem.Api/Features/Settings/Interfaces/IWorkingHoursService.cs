using RestaurantSystem.Api.Features.Settings.Dtos;

namespace RestaurantSystem.Api.Features.Settings.Interfaces;

public interface IWorkingHoursService
{
    Task<List<WorkingHoursDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<WorkingHoursDto?> GetByDayAsync(DayOfWeek dayOfWeek, CancellationToken cancellationToken = default);
    Task<WorkingHoursDto> UpdateAsync(UpdateWorkingHoursDto dto, CancellationToken cancellationToken = default);
    Task<bool> IsOpenNowAsync(CancellationToken cancellationToken = default);
    Task<WorkingHoursDto?> GetTodayHoursAsync(CancellationToken cancellationToken = default);
}
