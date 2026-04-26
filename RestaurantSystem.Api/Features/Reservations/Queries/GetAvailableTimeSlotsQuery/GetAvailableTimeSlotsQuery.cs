using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery;

public record GetAvailableTimeSlotsQuery(
    DateTime Date,
    int NumberOfGuests
) : IQuery<ApiResponse<AvailableTimeSlotsDto>>;

public class GetAvailableTimeSlotsQueryHandler : IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetAvailableTimeSlotsQueryHandler> _logger;

    private static readonly int SlotDurationMinutes = 120; // 2 hours per reservation

    public GetAvailableTimeSlotsQueryHandler(ApplicationDbContext context, ILogger<GetAvailableTimeSlotsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<AvailableTimeSlotsDto>> Handle(GetAvailableTimeSlotsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            // Validate date is not in the past
            if (query.Date.Date < DateTime.UtcNow.Date)
            {
                return ApiResponse<AvailableTimeSlotsDto>.Failure("Cannot make reservations for past dates");
            }

            // Get working hours for this day of week
            var dayOfWeek = query.Date.DayOfWeek;
            var workingHours = await _context.WorkingHours
                .FirstOrDefaultAsync(wh => wh.DayOfWeek == dayOfWeek && wh.IsActive, cancellationToken);

            // If restaurant is closed on this day or working hours not configured
            if (workingHours == null || workingHours.IsClosed)
            {
                _logger.LogInformation("Restaurant is closed on {DayOfWeek} ({Date})", dayOfWeek, query.Date.Date);
                return ApiResponse<AvailableTimeSlotsDto>.SuccessWithData(new AvailableTimeSlotsDto
                {
                    Date = query.Date,
                    TimeSlots = new List<TimeSlotDto>() // Empty - no slots available
                });
            }

            // Use configured opening/closing times from database
            var openingTime = workingHours.OpenTime;
            var closingTime = workingHours.CloseTime;

            _logger.LogInformation("Using working hours for {DayOfWeek}: {OpenTime} - {CloseTime}",
                dayOfWeek, openingTime, closingTime);

            // Get ALL active tables (not filtered by capacity)
            var allTables = await _context.Tables
                .Where(t => t.IsActive)
                .ToListAsync(cancellationToken);

            if (!allTables.Any())
            {
                return ApiResponse<AvailableTimeSlotsDto>.Failure("No active tables found");
            }

            // Get all confirmed/pending reservations for the requested date
            var queryDateUtc = DateTime.SpecifyKind(query.Date.Date, DateTimeKind.Utc);
            var existingReservations = await _context.Reservations
                .Where(r => r.ReservationDate.Date == queryDateUtc &&
                           (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
                .ToListAsync(cancellationToken);

            // For today's date, filter out past time slots
            var now = DateTime.UtcNow;
            var isToday = query.Date.Date == now.Date;
            var currentTimeSpan = isToday ? now.TimeOfDay : TimeSpan.Zero;

            // Generate time slots
            var timeSlots = new List<TimeSlotDto>();
            var currentTime = openingTime;

            while (currentTime.Add(TimeSpan.FromMinutes(SlotDurationMinutes)) <= closingTime)
            {
                var slotEndTime = currentTime.Add(TimeSpan.FromMinutes(SlotDurationMinutes));

                // Skip past time slots for today
                if (isToday && currentTime <= currentTimeSpan)
                {
                    currentTime = currentTime.Add(TimeSpan.FromMinutes(30));
                    continue;
                }

                // Find available tables for this time slot
                var availableTables = allTables.Where(table =>
                {
                    // Check if this table has any conflicting reservations
                    var hasConflict = existingReservations.Any(r =>
                        r.TableId == table.Id &&
                        DoTimeSlotsOverlap(currentTime, slotEndTime, r.StartTime, r.EndTime));

                    return !hasConflict;
                })
                .Select(t => new TableDto
                {
                    Id = t.Id,
                    TableNumber = t.TableNumber,
                    MaxGuests = t.MaxGuests,
                    IsActive = t.IsActive,
                    IsOutdoor = t.IsOutdoor,
                    PositionX = t.PositionX,
                    PositionY = t.PositionY,
                    Width = t.Width,
                    Height = t.Height,
                    Shape = t.Shape,
                    Notes = t.Notes,
                    QRCodeData = t.QRCodeData,
                    QRCodeGeneratedAt = t.QRCodeGeneratedAt
                })
                .ToList();

                // Only add time slots that have at least one available table
                if (availableTables.Any())
                {
                    timeSlots.Add(new TimeSlotDto
                    {
                        StartTime = currentTime,
                        EndTime = slotEndTime,
                        AvailableTables = availableTables
                    });
                }

                // Move to next slot (30-minute intervals)
                currentTime = currentTime.Add(TimeSpan.FromMinutes(30));
            }

            var result = new AvailableTimeSlotsDto
            {
                Date = query.Date,
                TimeSlots = timeSlots
            };

            return ApiResponse<AvailableTimeSlotsDto>.SuccessWithData(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available time slots for date {Date}, NumberOfGuests {NumberOfGuests}. Exception: {ExceptionMessage}", query.Date, query.NumberOfGuests, ex.Message);
            return ApiResponse<AvailableTimeSlotsDto>.Failure("Failed to retrieve available time slots");
        }
    }

    private static bool DoTimeSlotsOverlap(TimeSpan start1, TimeSpan end1, TimeSpan start2, TimeSpan end2)
    {
        // Two time slots overlap if one starts before the other ends
        return start1 < end2 && end1 > start2;
    }
}
