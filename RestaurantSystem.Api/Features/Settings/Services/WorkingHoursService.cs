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

    public WorkingHoursService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
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
        var now = DateTime.UtcNow;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"));
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
        var now = DateTime.UtcNow;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"));
        var currentDay = localTime.DayOfWeek;

        return await GetByDayAsync(currentDay, cancellationToken);
    }
}
