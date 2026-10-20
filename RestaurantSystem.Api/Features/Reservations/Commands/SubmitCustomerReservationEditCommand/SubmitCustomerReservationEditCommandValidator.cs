using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationEditCommand;

public sealed class SubmitCustomerReservationEditCommandValidator : AbstractValidator<SubmitCustomerReservationEditCommand>
{
    public SubmitCustomerReservationEditCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationEditDtoValidator());
        RuleFor(value => value.Request.PayloadHash).NotEmpty().Matches("^[a-f0-9]{64}$").When(value => value.Request is not null);
        RuleFor(value => value.Request.OwnerProof).NotEmpty().MaximumLength(CustomerReservationProofService.MaximumProofLength)
            .When(value => value.Request is not null);
    }
}
