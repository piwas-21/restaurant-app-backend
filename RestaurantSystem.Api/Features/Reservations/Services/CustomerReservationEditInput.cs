using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationEditInput
{
    // Share the create contract's contact, calendar and party constraints without accepting staff fields.
    public static CustomerReservationCreateDto ToCreate(CustomerReservationEditDto request) => new(
        request.SchemaVersion, request.OperationId, request.TenantId, request.TimeZone, request.Date,
        request.StartTime, request.EndTime, request.NumberOfGuests, request.CustomerName, request.CustomerEmail,
        request.CustomerPhone, request.SpecialRequests, request.PayloadHash, request.OwnerProof);
}
