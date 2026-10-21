namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationEditReadRegistration
{
    public static IServiceCollection AddCustomerReservationEditReads(this IServiceCollection services) =>
        services.AddScoped<ICustomerReservationEditReadAccess, CustomerReservationEditReadAccess>();
}
