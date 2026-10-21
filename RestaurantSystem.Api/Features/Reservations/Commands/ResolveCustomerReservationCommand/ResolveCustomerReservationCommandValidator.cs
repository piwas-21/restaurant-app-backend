using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCommand;

public sealed class ResolveCustomerReservationCommandValidator : AbstractValidator<ResolveCustomerReservationCommand>
{
    public ResolveCustomerReservationCommandValidator()
    {
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerReservationProofDtoValidator());
    }
}
