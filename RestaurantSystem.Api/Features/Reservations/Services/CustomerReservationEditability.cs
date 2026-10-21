using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationEditability
{
    public static void RequireFuture(ReservationStatus status, DateOnly day, TimeSpan start, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        if (status is not (ReservationStatus.Pending or ReservationStatus.Confirmed)
            || day < today || day == today && start <= now.TimeOfDay)
            throw new ConflictException("This booking can no longer be changed here. Check its current details.");
    }
}
