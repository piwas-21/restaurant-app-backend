using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public sealed class CustomerReservationEditDtoValidator : AbstractValidator<CustomerReservationEditDto>
{
    public CustomerReservationEditDtoValidator()
    {
        RuleFor(value => value.ReservationId).NotEmpty();
        RuleFor(value => CustomerReservationEditInput.ToCreate(value)).SetValidator(new CustomerReservationCreateDtoValidator());
    }
}
