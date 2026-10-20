using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationCancellationCommand;

public sealed class SubmitCustomerReservationCancellationCommandValidator : AbstractValidator<SubmitCustomerReservationCancellationCommand>
{
    public SubmitCustomerReservationCancellationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationProofDtoValidator());
    }
}
