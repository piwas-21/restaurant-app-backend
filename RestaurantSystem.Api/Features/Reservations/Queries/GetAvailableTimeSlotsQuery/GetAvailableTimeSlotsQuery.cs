using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery;

public record GetAvailableTimeSlotsQuery(
    DateTime Date,
    int NumberOfGuests
) : IQuery<ApiResponse<AvailableTimeSlotsDto>>;

public class GetAvailableTimeSlotsQueryHandler : IQueryHandler<GetAvailableTimeSlotsQuery, ApiResponse<AvailableTimeSlotsDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantClock _clock;
    private readonly ILogger<GetAvailableTimeSlotsQueryHandler> _logger;

    private static readonly int SlotDurationMinutes = 120; // 2 hours per reservation

    public GetAvailableTimeSlotsQueryHandler(
        ApplicationDbContext context,
        ITenantClock clock,
        ILogger<GetAvailableTimeSlotsQueryHandler> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AvailableTimeSlotsDto>> Handle(GetAvailableTimeSlotsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            // The requested date is a CALENDAR DAY on the restaurant's own wall, and so is every
            // opening hour and generated slot below — so "today" and "now" have to be read on the
            // tenant's clock too, not on UTC (backend #369). Read once: two reads can straddle
            // midnight and answer as two different days.
            var now = _clock.Now;

            // Validate date is not in the past
            if (query.Date.Date < now.Date)
            {
                return ApiResponse<AvailableTimeSlotsDto>.Failure("Cannot make reservations for past dates");
            }

            // Get working hours for this day of week
            var dayOfWeek = query.Date.DayOfWeek;
            var workingHours = await _context.WorkingHours
                .AsNoTracking()
                .Include(wh => wh.Shifts)
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

            // Every serving window of the day, not one interval. A restaurant that shuts between
            // lunch and dinner used to be read as one 11:00-23:00 span here, which offered a table
            // at 16:00 in an empty dining room (G11).
            var servingWindows = WorkingHoursWindows.Of(workingHours);

            _logger.LogInformation("Using working hours for {DayOfWeek}: {Windows}",
                dayOfWeek,
                string.Join(", ", servingWindows.Select(w => $"{w.Open}-{w.Close}")));

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

            // For today's date, filter out past time slots. `now.TimeOfDay` used to be UTC, which
            // in Zurich summer still offered the 18:30 slot at 20:00 local — a guest could book a
            // table two hours in the past (#369).
            var isToday = query.Date.Date == now.Date;
            var currentTimeSpan = isToday ? now.TimeOfDay : TimeSpan.Zero;

            // Generate time slots WINDOW BY WINDOW. Generating over the day's whole span is what
            // offered a table at 16:00 to a restaurant that serves 11:00-15:00 and 18:00-23:00: the
            // closure now produces no slots at all, because it belongs to no window (G11).
            var timeSlots = new List<TimeSlotDto>();

            foreach (var window in servingWindows)
            {
                var currentTime = window.Open;

                while (currentTime.Add(TimeSpan.FromMinutes(SlotDurationMinutes)) <= window.Close)
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
