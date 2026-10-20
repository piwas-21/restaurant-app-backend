using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationEditCommand;

public sealed class ResolveCustomerReservationEditCommandValidator : AbstractValidator<ResolveCustomerReservationEditCommand>
{
    public ResolveCustomerReservationEditCommandValidator()
    {
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerReservationProofDtoValidator());
    }
}
