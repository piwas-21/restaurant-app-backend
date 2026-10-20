using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationAssignedSlots
{
    public static bool Fits(IEnumerable<TableDto> tables, IReadOnlyList<Guid> assigned, int guests)
    {
        var available = tables.Where(table => assigned.Contains(table.Id) && table.IsActive && table.MaxGuests > 0)
            .DistinctBy(table => table.Id).ToArray();
        return available.Length == assigned.Count && available.Sum(table => (long)table.MaxGuests) >= guests;
    }
}
