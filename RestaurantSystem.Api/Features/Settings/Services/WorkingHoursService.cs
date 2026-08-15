using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.Services;

public class WorkingHoursService : IWorkingHoursService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITenantClock _clock;

    public WorkingHoursService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ITenantClock clock)
    {
        _context = context;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<List<WorkingHoursDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var workingHours = await _context.WorkingHours
            .OrderBy(wh => wh.DayOfWeek)
            .ToListAsync(cancellationToken);

        return workingHours.Select(wh => new WorkingHoursDto
        {
            Id = wh.Id,
            DayOfWeek = wh.DayOfWeek,
            OpenTime = wh.OpenTime,
            CloseTime = wh.CloseTime,
            IsActive = wh.IsActive,
            IsClosed = wh.IsClosed,
            Notes = wh.Notes
        }).ToList();
    }

    public async Task<WorkingHoursDto?> GetByDayAsync(DayOfWeek dayOfWeek, CancellationToken cancellationToken = default)
    {
        var workingHour = await _context.WorkingHours
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == dayOfWeek, cancellationToken);

        if (workingHour == null)
            return null;

        return new WorkingHoursDto
        {
            Id = workingHour.Id,
            DayOfWeek = workingHour.DayOfWeek,
            OpenTime = workingHour.OpenTime,
            CloseTime = workingHour.CloseTime,
            IsActive = workingHour.IsActive,
            IsClosed = workingHour.IsClosed,
            Notes = workingHour.Notes
        };
    }

    public async Task<WorkingHoursDto> UpdateAsync(UpdateWorkingHoursDto dto, CancellationToken cancellationToken = default)
    {
        var workingHour = await _context.WorkingHours
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == dto.DayOfWeek, cancellationToken);

        if (workingHour == null)
        {
            throw new NotFoundException($"Working hours not found for {dto.DayOfWeek}");
        }

        workingHour.OpenTime = dto.OpenTime;
        workingHour.CloseTime = dto.CloseTime;
        workingHour.IsActive = dto.IsActive;
        workingHour.IsClosed = dto.IsClosed;
        workingHour.Notes = dto.Notes;
        workingHour.UpdatedAt = DateTime.UtcNow;
        workingHour.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return new WorkingHoursDto
        {
            Id = workingHour.Id,
            DayOfWeek = workingHour.DayOfWeek,
            OpenTime = workingHour.OpenTime,
            CloseTime = workingHour.CloseTime,
            IsActive = workingHour.IsActive,
            IsClosed = workingHour.IsClosed,
            Notes = workingHour.Notes
        };
    }

    public async Task<bool> IsOpenNowAsync(CancellationToken cancellationToken = default)
    {
        // The zone is the tenant's configuration, not a constant: this line used to hardcode
        // "Europe/Zurich", which answers "are we open" on Geneva's clock for every tenant the
        // platform ever provisions (#363).
        var localTime = _clock.Now;
        var currentDay = localTime.DayOfWeek;
        var currentTime = localTime.TimeOfDay;

        var todayHours = await _context.WorkingHours
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == currentDay, cancellationToken);

        if (todayHours == null || !todayHours.IsActive || todayHours.IsClosed)
            return false;

        return currentTime >= todayHours.OpenTime && currentTime <= todayHours.CloseTime;
    }

    public async Task<WorkingHoursDto?> GetTodayHoursAsync(CancellationToken cancellationToken = default)
    {
        var currentDay = _clock.Now.DayOfWeek;

        return await GetByDayAsync(currentDay, cancellationToken);
    }
}
