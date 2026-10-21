using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCancellationCommand;

public sealed class ResolveCustomerReservationCancellationCommandValidator : AbstractValidator<ResolveCustomerReservationCancellationCommand>
{
    public ResolveCustomerReservationCancellationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationProofDtoValidator());
    }
}
