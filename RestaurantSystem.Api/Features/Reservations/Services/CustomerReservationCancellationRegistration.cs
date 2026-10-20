namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationCancellationRegistration
{
    public static IServiceCollection AddCustomerReservationCancellation(this IServiceCollection services) => services
        .AddScoped<ICustomerReservationCancellationProofs, CustomerReservationCancellationProofs>()
        .AddScoped<ICustomerReservationCancellationAccess, CustomerReservationCancellationAccess>()
        .AddScoped<ICustomerReservationCancellationService, CustomerReservationCancellationService>();
}
