using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Entities;
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
            .Include(wh => wh.Shifts)
            .OrderBy(wh => wh.DayOfWeek)
            .ToListAsync(cancellationToken);

        return workingHours.Select(MapToDto).ToList();
    }

    public async Task<WorkingHoursDto?> GetByDayAsync(DayOfWeek dayOfWeek, CancellationToken cancellationToken = default)
    {
        var workingHour = await _context.WorkingHours
            .Include(wh => wh.Shifts)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == dayOfWeek, cancellationToken);

        return workingHour == null ? null : MapToDto(workingHour);
    }

    public async Task<WorkingHoursDto> UpdateAsync(UpdateWorkingHoursDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Validate BEFORE loading: a refused body must not have touched the tracker.
        var shifts = WorkingHoursShiftRules.Resolve(dto);

        var workingHour = await _context.WorkingHours
            .Include(wh => wh.Shifts)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == dto.DayOfWeek, cancellationToken);

        if (workingHour == null)
        {
            throw new NotFoundException($"Working hours not found for {dto.DayOfWeek}");
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        // Replace, not diff. A shift row carries nothing but two times, and no other table points
        // at one, so there is no history to preserve — unlike ProductIngredient, where the same
        // delete-and-recreate silently rewrote the ingredient detail of every past order because
        // OrderItem holds those ids.
        _context.WorkingHoursShifts.RemoveRange(workingHour.Shifts);
        workingHour.Shifts.Clear();

        foreach (var shift in shifts)
        {
            workingHour.Shifts.Add(new WorkingHoursShift
            {
                WorkingHoursId = workingHour.Id,
                OpenTime = shift.OpenTime,
                CloseTime = shift.CloseTime,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            });
        }

        // The legacy mirror: the FIRST window BY OPENING TIME, not by the order they were typed —
        // `shifts` is already sorted by WorkingHoursShiftRules.Resolve. A client that has never
        // heard of shifts then under-reports the day instead of claiming the shop is open through
        // the closure. An empty list only reaches here on a closed day, where the incoming pair is
        // stored so the times survive the closed/open toggle.
        workingHour.OpenTime = shifts.Count > 0 ? shifts[0].OpenTime : dto.OpenTime;
        workingHour.CloseTime = shifts.Count > 0 ? shifts[0].CloseTime : dto.CloseTime;
        workingHour.IsActive = dto.IsActive;
        workingHour.IsClosed = dto.IsClosed;
        workingHour.Notes = dto.Notes;
        workingHour.UpdatedAt = DateTime.UtcNow;
        workingHour.UpdatedBy = auditIdentifier;

        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(workingHour);
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
            .Include(wh => wh.Shifts)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == currentDay, cancellationToken);

        if (todayHours == null || !todayHours.IsActive || todayHours.IsClosed)
            return false;

        // ANY window, not "between the first open and the last close": a restaurant serving
        // 11:00-15:00 and 18:00-23:00 is shut at 16:00, and the old single-interval test said it
        // was open (G11).
        return WorkingHoursWindows.Contains(todayHours, currentTime);
    }

    public async Task<WorkingHoursDto?> GetTodayHoursAsync(CancellationToken cancellationToken = default)
    {
        var currentDay = _clock.Now.DayOfWeek;

        return await GetByDayAsync(currentDay, cancellationToken);
    }

    private static WorkingHoursDto MapToDto(WorkingHours wh) => new()
    {
        Id = wh.Id,
        DayOfWeek = wh.DayOfWeek,
        OpenTime = wh.OpenTime,
        CloseTime = wh.CloseTime,
        Shifts = wh.Shifts
            .OrderBy(s => s.OpenTime)
            .Select(s => new WorkingHoursShiftDto { OpenTime = s.OpenTime, CloseTime = s.CloseTime })
            .ToList(),
        IsActive = wh.IsActive,
        IsClosed = wh.IsClosed,
        Notes = wh.Notes
    };
}
