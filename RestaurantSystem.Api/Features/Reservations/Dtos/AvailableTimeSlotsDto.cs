namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record AvailableTimeSlotsDto
{
    public DateTime Date { get; set; }
    public List<TimeSlotDto> TimeSlots { get; set; } = new();
}

public record TimeSlotDto
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public List<TableDto> AvailableTables { get; set; } = new();
}
