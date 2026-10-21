namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationEditRegistration
{
    public static IServiceCollection AddCustomerReservationEdits(this IServiceCollection services) => services
        .AddScoped<ICustomerReservationEditProofs, CustomerReservationEditProofs>()
        .AddScoped<ICustomerReservationEditor, CustomerReservationEditor>()
        .AddScoped<ICustomerReservationEditService, CustomerReservationEditService>();
}
