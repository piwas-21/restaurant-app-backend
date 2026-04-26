using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;

namespace RestaurantSystem.Api.Features.Settings;

[ApiController]
[Route("api/[controller]")]
public class WorkingHoursController : ControllerBase
{
    private readonly IWorkingHoursService _service;

    public WorkingHoursController(IWorkingHoursService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get all working hours (public endpoint)
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ApiResponse<List<WorkingHoursDto>>> GetAll(CancellationToken cancellationToken)
    {
        var workingHours = await _service.GetAllAsync(cancellationToken);
        return ApiResponse<List<WorkingHoursDto>>.SuccessWithData(workingHours);
    }

    /// <summary>
    /// Get working hours for specific day (public endpoint)
    /// </summary>
    [HttpGet("{dayOfWeek}")]
    [AllowAnonymous]
    public async Task<ApiResponse<WorkingHoursDto>> GetByDay(DayOfWeek dayOfWeek, CancellationToken cancellationToken)
    {
        var workingHour = await _service.GetByDayAsync(dayOfWeek, cancellationToken);

        if (workingHour == null)
            return ApiResponse<WorkingHoursDto>.Failure($"Working hours not found for {dayOfWeek}");

        return ApiResponse<WorkingHoursDto>.SuccessWithData(workingHour);
    }

    /// <summary>
    /// Check if restaurant is currently open (public endpoint)
    /// </summary>
    [HttpGet("is-open")]
    [AllowAnonymous]
    public async Task<ApiResponse<bool>> IsOpen(CancellationToken cancellationToken)
    {
        var isOpen = await _service.IsOpenNowAsync(cancellationToken);
        return ApiResponse<bool>.SuccessWithData(isOpen);
    }

    /// <summary>
    /// Get today's working hours (public endpoint)
    /// </summary>
    [HttpGet("today")]
    [AllowAnonymous]
    public async Task<ApiResponse<WorkingHoursDto>> GetToday(CancellationToken cancellationToken)
    {
        var todayHours = await _service.GetTodayHoursAsync(cancellationToken);

        if (todayHours == null)
            return ApiResponse<WorkingHoursDto>.Failure("Working hours not found for today");

        return ApiResponse<WorkingHoursDto>.SuccessWithData(todayHours);
    }

    /// <summary>
    /// Update working hours (admin only)
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<WorkingHoursDto>> Update(
        [FromBody] UpdateWorkingHoursDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.UpdateAsync(dto, cancellationToken);
            return ApiResponse<WorkingHoursDto>.SuccessWithData(
                updated,
                "Working hours updated successfully"
            );
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkingHoursDto>.Failure(ex.Message);
        }
    }
}
